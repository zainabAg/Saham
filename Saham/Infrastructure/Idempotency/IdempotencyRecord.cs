using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Infrastructure.Idempotency;

/* Stores the first response of a request sent with an Idempotency-Key header
 * (SRS EX-11, table idempotency_keys). Unique on (UserId, Scope, Key). */
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

    public IdempotencyRecord(Guid id, Guid userId, string scope, string key, string requestHash, DateTime now)
        : base(id)
    {
        UserId = userId;
        Scope = scope;
        Key = key;
        RequestHash = requestHash;
        Status = IdempotencyStatus.InProgress;
        CreationTime = now;
    }

    public void Complete(int statusCode, string? responseBody)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public enum IdempotencyStatus
{
    InProgress = 0,
    Completed = 1
}
