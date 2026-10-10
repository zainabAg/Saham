using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Identity;

/* A one-time login code sent by SMS (SRS table otp_challenges). Only the hash is stored. */
public class OtpChallenge : AggregateRoot<Guid>
{
    public string PhoneNumber { get; private set; }

    public string CodeHash { get; private set; }

    public OtpPurpose Purpose { get; private set; }

    public int Attempts { get; private set; }

    public DateTime CreationTime { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? ConsumedAt { get; private set; }

    protected OtpChallenge()
    {
    }

    public OtpChallenge(Guid id, string phoneNumber, string codeHash, OtpPurpose purpose, DateTime now, DateTime expiresAt)
        : base(id)
    {
        PhoneNumber = phoneNumber;
        CodeHash = codeHash;
        Purpose = purpose;
        CreationTime = now;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    public bool IsConsumed => ConsumedAt.HasValue;

    public void RegisterFailedAttempt() => Attempts++;

    public void Consume(DateTime now) => ConsumedAt = now;
}
