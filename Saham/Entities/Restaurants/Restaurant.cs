using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using Saham.Entities.Shared;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Restaurants;

/* A partner restaurant (SRS table restaurants). The owner onboards it and sets the
 * commission (restaurant.onboard); the restaurant manager runs the menu, hours,
 * minimum order and temporary pause on their own (S-07, BR-050). */
public class Restaurant : FullAuditedAggregateRoot<Guid>
{
    public const int PhoneMaxLength = 20;
    public const int AddressTextMaxLength = 512;
    public const int LogoUrlMaxLength = 512;
    public const int PausedReasonMaxLength = 256;
    public const int MaxCommissionRateBp = 10_000;
    public const int DefaultCommissionRateBp = 1_000; // 10% (DEC-05)

    private const int MinutesPerWeek = 7 * 24 * 60;

    public Guid CityId { get; private set; }

    public I18nText Name { get; private set; }

    public string Phone { get; private set; }

    public Point Location { get; private set; }

    public string AddressText { get; private set; }

    public string? LogoUrl { get; private set; }

    public RestaurantStatus Status { get; private set; }

    public bool IsPaused { get; private set; }

    public string? PausedReason { get; private set; }

    public DateTime? PausedUntil { get; private set; }

    public int DefaultPrepMinutes { get; private set; }

    public long MinOrderAmount { get; private set; }

    /* Commission in basis points: 1000 = 10%. Changed by the owner only. */
    public int CommissionRateBp { get; private set; }

    public DateTime? TabletLastSeenAt { get; private set; }

    public ICollection<RestaurantHours> Hours { get; private set; }

    protected Restaurant()
    {
        Name = null!;
        Phone = null!;
        Location = null!;
        AddressText = null!;
        Hours = new List<RestaurantHours>();
    }

    public Restaurant(
        Guid id,
        Guid cityId,
        I18nText name,
        string phone,
        Point location,
        string addressText,
        int commissionRateBp = DefaultCommissionRateBp)
        : base(id)
    {
        CityId = cityId;
        Hours = new List<RestaurantHours>();
        Status = RestaurantStatus.Pending;
        DefaultPrepMinutes = 20;
        Name = null!;
        Phone = null!;
        Location = null!;
        AddressText = null!;
        UpdateProfile(name, phone, location, addressText, null);
        SetCommissionRate(commissionRateBp);
    }

    public void UpdateProfile(I18nText name, string phone, Point location, string addressText, string? logoUrl)
    {
        Name = name;
        Phone = Check.NotNullOrWhiteSpace(phone, nameof(phone), PhoneMaxLength);
        Location = location;
        AddressText = Check.NotNullOrWhiteSpace(addressText, nameof(addressText), AddressTextMaxLength).Trim();
        LogoUrl = logoUrl.IsNullOrWhiteSpace() ? null : Check.Length(logoUrl!.Trim(), nameof(logoUrl), LogoUrlMaxLength);
    }

    public void SetCommissionRate(int commissionRateBp)
    {
        if (commissionRateBp is < 0 or > MaxCommissionRateBp)
        {
            throw new BusinessException(SahamErrorCodes.InvalidCommissionRate);
        }

        CommissionRateBp = commissionRateBp;
    }

    /* DER-RST-05: applies to new quotes only (quotes copy the value). */
    public void SetMinOrderAmount(long amount)
    {
        if (amount < 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidAmount);
        }

        MinOrderAmount = amount;
    }

    public void SetDefaultPrepMinutes(int minutes, int min, int max)
    {
        if (minutes < min || minutes > max)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPrepMinutes).WithData("min", min).WithData("max", max);
        }

        DefaultPrepMinutes = minutes;
    }

    public void Activate() => Status = RestaurantStatus.Active;

    public void Suspend() => Status = RestaurantStatus.Suspended;

    /* REQ-RST-09 / BR-049: stops new orders immediately; accepted orders continue. */
    public void Pause(string? reason, DateTime? until, DateTime now)
    {
        if (until.HasValue && until.Value <= now)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPauseUntil);
        }

        IsPaused = true;
        PausedReason = reason.IsNullOrWhiteSpace() ? null : Check.Length(reason!.Trim(), nameof(reason), PausedReasonMaxLength);
        PausedUntil = until;
    }

    public void Resume()
    {
        IsPaused = false;
        PausedReason = null;
        PausedUntil = null;
    }

    /* A pause with a reopening time ends by itself. */
    public bool IsPausedAt(DateTime now) => IsPaused && (!PausedUntil.HasValue || PausedUntil.Value > now);

    public void MarkTabletSeen(DateTime now) => TabletLastSeenAt = now;

    /* Replaces the weekly schedule. A shift whose closing time is earlier than its opening
     * time continues after midnight (18:00 → 02:00, REQ-RST-08). Shifts may not overlap. */
    public void SetHours(IEnumerable<(DayOfWeek Day, TimeOnly OpensAt, TimeOnly ClosesAt)> shifts, Func<Guid> newId)
    {
        var list = shifts.ToList();
        if (list.Any(s => s.OpensAt == s.ClosesAt))
        {
            throw new BusinessException(SahamErrorCodes.InvalidOpeningHours);
        }

        var intervals = list.SelectMany(s => ToWeekIntervals(s.Day, s.OpensAt, s.ClosesAt)).OrderBy(i => i.Start).ToList();
        for (var i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Start < intervals[i - 1].End)
            {
                throw new BusinessException(SahamErrorCodes.InvalidOpeningHours);
            }
        }

        Hours.Clear();
        foreach (var shift in list.OrderBy(s => s.Day).ThenBy(s => s.OpensAt))
        {
            Hours.Add(new RestaurantHours(newId(), Id, shift.Day, shift.OpensAt, shift.ClosesAt));
        }
    }

    /* BR-001: within opening hours at the given local time of the restaurant's city. */
    public bool IsOpenAt(DateTime localTime)
    {
        var minute = MinuteOfWeek(localTime);
        return Hours
            .SelectMany(h => ToWeekIntervals(h.DayOfWeek, h.OpensAt, h.ClosesAt))
            .Any(i => minute >= i.Start && minute < i.End);
    }

    /* Next opening time (local) for the "closed — opens at …" badge; null if no hours. */
    public DateTime? GetNextOpeningAt(DateTime localTime)
    {
        if (Hours.Count == 0)
        {
            return null;
        }

        var minute = MinuteOfWeek(localTime);
        var minutesAhead = Hours
            .Select(h => (MinuteOfWeek(h.DayOfWeek, h.OpensAt) - minute + MinutesPerWeek) % MinutesPerWeek)
            .Where(d => d > 0)
            .DefaultIfEmpty(MinutesPerWeek)
            .Min();

        var start = new DateTime(localTime.Year, localTime.Month, localTime.Day, localTime.Hour, localTime.Minute, 0, localTime.Kind);
        return start.AddMinutes(minutesAhead);
    }

    /* Whether customers can order right now: active, not paused, and open (BR-001, BR-049). */
    public bool AcceptsOrdersAt(DateTime utcNow, DateTime localTime)
    {
        return Status == RestaurantStatus.Active && !IsPausedAt(utcNow) && IsOpenAt(localTime);
    }

    private static int MinuteOfWeek(DateTime time) => MinuteOfWeek(time.DayOfWeek, TimeOnly.FromDateTime(time));

    private static int MinuteOfWeek(DayOfWeek day, TimeOnly time) => (int)day * 24 * 60 + time.Hour * 60 + time.Minute;

    /* A shift as [start, end) minutes of the week; a shift that wraps past the end of the week is split. */
    private static IEnumerable<(int Start, int End)> ToWeekIntervals(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        var start = MinuteOfWeek(day, opensAt);
        var length = (int)(closesAt - opensAt).TotalMinutes; // TimeOnly subtraction wraps around midnight
        var end = start + length;

        if (end <= MinutesPerWeek)
        {
            yield return (start, end);
        }
        else
        {
            yield return (start, MinutesPerWeek);
            yield return (0, end - MinutesPerWeek);
        }
    }
}
