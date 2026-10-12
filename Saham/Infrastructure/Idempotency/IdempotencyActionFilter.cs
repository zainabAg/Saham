using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Json;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace Saham.Infrastructure.Idempotency;

/* SRS EX-11 for app service methods marked [Idempotent] — Phase 5, all inside the request's own
 * ABP unit of work (READ COMMITTED), so the operation and its saved result commit or roll back together:
 *   1. a transaction advisory lock on (tenant, user, scope, key); taken → 409 RequestInProgress (no wait);
 *   2. a saved result: same request fingerprint → the same status and body, the operation does not run;
 *      another fingerprint → 409 IdempotencyConflict;
 *   3. otherwise the operation runs and its 2xx result is saved in the same transaction, before commit.
 * A failure anywhere rolls everything back, so a retry runs again; a commit whose response was lost
 * replays on retry. Nothing is written in a separate transaction, so no "in progress" row can be left
 * behind by a crash. Authorization runs before this filter on every attempt, replays included.
 * Scope = HTTP method + request path, so the same key on two orders' cancellations are two operations.
 * Registered after ABP's unit-of-work filter (SahamModule.ConfigureIdempotency). */
public class IdempotencyActionFilter : IAsyncActionFilter, ITransientDependency
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    private const int MaxKeyLength = 128;
    private const int MaxScopeLength = 256;

    private readonly IRepository<IdempotencyRecord, Guid> _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IJsonSerializer _jsonSerializer;

    public IdempotencyActionFilter(
        IRepository<IdempotencyRecord, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IClock clock,
        IGuidGenerator guidGenerator,
        IJsonSerializer jsonSerializer)
    {
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _clock = clock;
        _guidGenerator = guidGenerator;
        _jsonSerializer = jsonSerializer;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor ||
            !descriptor.MethodInfo.IsDefined(typeof(IdempotentAttribute), inherit: true))
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (!IsValidKey(key))
        {
            throw new BusinessException(SahamErrorCodes.IdempotencyKeyRequired);
        }

        if (_unitOfWorkManager.Current is not { Options.IsTransactional: true })
        {
            throw new AbpException("[Idempotent] needs the request's transactional unit of work.");
        }

        // Identity from the server only (token and tenant resolution), never from the body.
        var userId = _currentUser.Id ?? Guid.Empty;
        var scope = ScopeOf(_currentTenant.Id, http.Request.Method, http.Request.Path.Value ?? string.Empty);
        var requestHash = Convert.ToHexString(RequestFingerprint.Compute(JsonSerializer.SerializeToUtf8Bytes(context.ActionArguments)));

        var db = await _repository.GetDbContextAsync();
        if (db.Database.CurrentTransaction == null)
        {
            throw new AbpException("[Idempotent] needs an open database transaction.");
        }

        var locked = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", LockIdOf(_currentTenant.Id, userId, scope, key))
            .SingleAsync();
        if (!locked)
        {
            http.Response.Headers.RetryAfter = "1";
            throw new BusinessException(SahamErrorCodes.RequestInProgress);
        }

        // After the lock, READ COMMITTED sees what an earlier holder committed.
        var existing = await _repository.FindAsync(x => x.UserId == userId && x.Scope == scope && x.Key == key);
        if (existing is { Status: IdempotencyStatus.InProgress })
        {
            // Left by the pre-Phase-5 filter (a separate "in progress" commit). This version never writes one,
            // and we hold the key's lock, so it belongs to a request that died: drop it and run normally.
            await _repository.DeleteAsync(existing, autoSave: true);
            existing = null;
        }

        if (existing != null)
        {
            context.Result = Replay(existing, requestHash, http);
            return;
        }

        var executed = await next();
        if (executed.Exception != null && !executed.ExceptionHandled)
        {
            return; // the unit of work rolls back; nothing was saved, a retry runs again
        }

        var (statusCode, body) = ReadResult(executed.Result);
        if (statusCode is < 200 or >= 300)
        {
            return;
        }

        // Same transaction, flushed now: a failure here fails the request and rolls the operation back.
        await _repository.InsertAsync(
            new IdempotencyRecord(_guidGenerator.Create(), userId, scope, key, requestHash, statusCode, body, _clock.Now),
            autoSave: true);
        http.Response.Headers.CacheControl = "no-store";
    }

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && key.All(c => c is >= '!' and <= '~');

    /* "POST:/api/app/customer-order/{id}/cancel" — the method and the real resource id; tenant first when there is one. */
    public static string ScopeOf(Guid? tenantId, string method, string path)
    {
        var scope = $"{(tenantId.HasValue ? tenantId.Value.ToString("N") + "|" : string.Empty)}{method.ToUpperInvariant()}:{path.ToLowerInvariant()}";
        return scope.Length <= MaxScopeLength ? scope : throw new BusinessException(SahamErrorCodes.IdempotencyKeyRequired);
    }

    /* One bigint per (tenant, user, scope, key); a length-safe encoding avoids delimiter ambiguity. The
     * single-bigint advisory key space does not overlap the (int, int) one of CustomerBalanceLock.
     * A collision only makes an unrelated request wait for a retry; it never replays another's result. */
    public static long LockIdOf(Guid? tenantId, Guid userId, string scope, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { tenantId?.ToString("D") ?? "host", userId.ToString("D"), scope, key })));
        return BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(0, 8));
    }

    private static IActionResult Replay(IdempotencyRecord record, string requestHash, HttpContext http)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(record.RequestHash), Encoding.ASCII.GetBytes(requestHash)))
        {
            throw new BusinessException(SahamErrorCodes.IdempotencyConflict);
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers[ReplayedHeaderName] = "true";
        if (record.ResponseBody == null)
        {
            return new StatusCodeResult(record.ResponseStatusCode ?? StatusCodes.Status204NoContent);
        }

        // The stored JSON as it is (jsonb: same values, key order may differ) — never serialized again.
        return new ContentResult
        {
            Content = record.ResponseBody,
            ContentType = "application/json; charset=utf-8",
            StatusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK
        };
    }

    private (int StatusCode, string? Body) ReadResult(IActionResult? result)
    {
        return result switch
        {
            ObjectResult objectResult => (
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                objectResult.Value == null ? null : _jsonSerializer.Serialize(objectResult.Value)),
            IStatusCodeActionResult statusResult => (statusResult.StatusCode ?? StatusCodes.Status204NoContent, null),
            _ => (StatusCodes.Status204NoContent, null)
        };
    }
}
