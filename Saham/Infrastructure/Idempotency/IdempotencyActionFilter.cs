using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace Saham.Infrastructure.Idempotency;

/* Implements SRS EX-11 for app service methods marked with [Idempotent]:
 * - same key + same body  -> the stored first response is returned, nothing runs again;
 * - same key + other body -> 409 IdempotencyConflict;
 * - first request still running -> 409 RequestInProgress.
 * Registered after ABP's unit-of-work filter, so the "completed" record is
 * committed in the same transaction as the business change. */
public class IdempotencyActionFilter : IAsyncActionFilter, ITransientDependency
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 128;

    private readonly IRepository<IdempotencyRecord, Guid> _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IJsonSerializer _jsonSerializer;

    public IdempotencyActionFilter(
        IRepository<IdempotencyRecord, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentUser currentUser,
        IClock clock,
        IGuidGenerator guidGenerator,
        IJsonSerializer jsonSerializer)
    {
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _currentUser = currentUser;
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

        var key = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (key.IsNullOrWhiteSpace() || key.Length > MaxKeyLength)
        {
            throw new BusinessException(SahamErrorCodes.IdempotencyKeyRequired);
        }

        var userId = _currentUser.Id ?? Guid.Empty;
        var scope = $"{descriptor.ControllerName}.{descriptor.ActionName}";
        var requestHash = ComputeHash(context);

        var existing = await FindAsync(userId, scope, key);
        if (existing != null)
        {
            context.Result = Replay(existing, requestHash);
            return;
        }

        var recordId = await TryInsertAsync(userId, scope, key, requestHash);
        if (recordId == null)
        {
            // Another request with the same key won the insert race.
            throw new BusinessException(SahamErrorCodes.RequestInProgress);
        }

        var executed = await next();

        if (executed.Exception != null && !executed.ExceptionHandled)
        {
            // The business transaction rolled back: free the key so the client can retry.
            await DeleteAsync(recordId.Value);
            return;
        }

        var (statusCode, body) = ReadResult(executed.Result);
        var record = await _repository.GetAsync(recordId.Value);
        record.Complete(statusCode, body);
        await _repository.UpdateAsync(record);
    }

    private IActionResult Replay(IdempotencyRecord record, string requestHash)
    {
        if (record.RequestHash != requestHash)
        {
            throw new BusinessException(SahamErrorCodes.IdempotencyConflict);
        }

        if (record.Status == IdempotencyStatus.InProgress)
        {
            throw new BusinessException(SahamErrorCodes.RequestInProgress);
        }

        if (record.ResponseBody == null)
        {
            return new StatusCodeResult(record.ResponseStatusCode ?? StatusCodes.Status204NoContent);
        }

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

    private static string ComputeHash(ActionExecutingContext context)
    {
        var json = JsonSerializer.Serialize(context.ActionArguments);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private async Task<IdempotencyRecord?> FindAsync(Guid userId, string scope, string key)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var record = await _repository.FindAsync(x => x.UserId == userId && x.Scope == scope && x.Key == key);
        await uow.CompleteAsync();
        return record;
    }

    private async Task<Guid?> TryInsertAsync(Guid userId, string scope, string key, string requestHash)
    {
        var record = new IdempotencyRecord(_guidGenerator.Create(), userId, scope, key, requestHash, _clock.Now);
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            await _repository.InsertAsync(record);
            await uow.CompleteAsync();
            return record.Id;
        }
        catch (DbUpdateException)
        {
            return null;
        }
    }

    private async Task DeleteAsync(Guid recordId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _repository.DeleteAsync(recordId);
        await uow.CompleteAsync();
    }
}
