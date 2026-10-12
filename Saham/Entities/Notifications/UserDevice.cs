using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Notifications;

/* A phone or tablet that receives push notifications (FCM registration token). The app registers
 * its token after sign-in and whenever Firebase rotates it, and unregisters it on sign-out.
 * One token = one device = one user at a time: a shared tablet follows whoever signed in last. */
public class UserDevice : CreationAuditedAggregateRoot<Guid>
{
    public const int TokenMaxLength = 512;

    public Guid UserId { get; private set; }

    public string Token { get; private set; }

    public DevicePlatform Platform { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    protected UserDevice()
    {
        Token = null!;
    }

    internal UserDevice(Guid id, Guid userId, string token, DevicePlatform platform, DateTime now)
        : base(id)
    {
        Token = Check.NotNullOrWhiteSpace(token, nameof(token), TokenMaxLength);
        Assign(userId, platform, now);
    }

    internal void Assign(Guid userId, DevicePlatform platform, DateTime now)
    {
        UserId = userId;
        Platform = platform;
        LastSeenAt = now;
    }
}

public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2
}
