using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Infrastructure.Idempotency;

/* Stores the first successful response of a request sent with an Idempotency-Key header
 * (SRS EX-11, table idempotency_keys). Unique on (UserId, Scope, Key). Since Phase 5 a row is written
 * only as Completed, in the same transaction as the operation; "in progress" is the advisory lock. */
public class IdempotencyRecord : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public string Scope { get; private set; }

    public string Key { get; private set; }

    public string RequestHash { get; private set; }

    public IdempotencyStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public DateTime CreationTime { get; private set; }

    protected IdempotencyRecord()
    {
    }

    public IdempotencyRecord(Guid id, Guid userId, string scope, string key, string requestHash,
        int statusCode, string? responseBody, DateTime now)
        : base(id)
    {
        UserId = userId;
        Scope = scope;
        Key = key;
        RequestHash = requestHash;
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = statusCode;
        ResponseBody = responseBody;
        CreationTime = now;
    }
}

public enum IdempotencyStatus
{
    InProgress = 0, // only rows left by the pre-Phase-5 filter; removed when their key is used again
    Completed = 1
}
