using System;

namespace Saham.Entities.Pricing;

/// <summary>
/// خوارزمية حتمية: نفس المدخلات تعطي نفس النتيجة، دون قاعدة بيانات أو ساعة داخلية.
/// تستخدم مسافة المشروع الحالية؛ لا تدعي أنها مسافة طريق أو بيانات ازدحام حقيقية.
/// </summary>
public static class EtaEstimator
{
    public sealed record Policy(
        decimal AverageSpeedKmh,
        int PickupBufferMinutes,
        int WindowMinutes,
        int LateGraceMinutes);

    public sealed record Estimate(
        DateTime MinAt,
        DateTime MaxAt,
        DateTime LateAfter,
        int TravelMinutes);

    /// <summary>الخط الأساسي لعرض السعر، مطابق لصيغة المشروع الحالية.</summary>
    public static Estimate ForQuote(
        DateTime nowUtc, int prepMinutes, int distanceMeters,
        Policy policy, DateTime? scheduledForUtc = null)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        Validate(policy);
        NonNegative(prepMinutes, nameof(prepMinutes));
        var travel = TravelMinutes(distanceMeters, policy.AverageSpeedKmh);

        if (scheduledForUtc is { } scheduled)
        {
            ValidateUtc(scheduled, nameof(scheduledForUtc));
            if (scheduled < nowUtc)
                throw new ArgumentOutOfRangeException(nameof(scheduledForUtc));
            // الموعد المجدول موعد وصول، وليس موعد بدء تجهيز.
            return Build(scheduled, travel, policy);
        }

        var total = checked(prepMinutes + policy.PickupBufferMinutes + travel);
        return Build(nowUtc.AddMinutes(total), travel, policy);
    }

    /// <summary>
    /// بعد القبول: تجهيز الطعام ووصول السائق قد يحدثان بالتوازي.
    /// إن لم يتوفر تقدير وصول موثوق، نستخدم هامش الاستلام بعد الجاهزية.
    /// الممرر مسؤول عن استبعاد موقع قديم وعدم تمرير وقت وصول غير موثوق.
    /// </summary>
    public static Estimate BeforePickup(
        DateTime nowUtc, DateTime readyAtUtc, int distanceMeters,
        Policy policy, DateTime? driverArrivalAtUtc = null)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        ValidateUtc(readyAtUtc, nameof(readyAtUtc));
        Validate(policy);
        var travel = TravelMinutes(distanceMeters, policy.AverageSpeedKmh);
        var ready = Later(nowUtc, readyAtUtc);
        DateTime pickup;

        if (driverArrivalAtUtc is { } arrival)
        {
            ValidateUtc(arrival, nameof(driverArrivalAtUtc));
            // ننتظر الطرف الأبطأ؛ لا نجمع وقتي الانتظار ونحتسبهما مرتين.
            pickup = Later(ready, Later(nowUtc, arrival));
        }
        else
        {
            pickup = ready.AddMinutes(policy.PickupBufferMinutes);
        }

        return Build(pickup.AddMinutes(travel), travel, policy);
    }

    /// <summary>بعد الاستلام: المسافة المتبقية فقط، دون تجهيز أو هامش استلام.</summary>
    public static Estimate AfterPickup(DateTime nowUtc, int remainingDistanceMeters, Policy policy)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        Validate(policy);
        var travel = TravelMinutes(remainingDistanceMeters, policy.AverageSpeedKmh);
        return Build(nowUtc.AddMinutes(travel), travel, policy);
    }

    /// <summary>تقريب للأعلى حتى لا نحذف جزء دقيقة من الرحلة.</summary>
    public static int TravelMinutes(int distanceMeters, decimal averageSpeedKmh)
    {
        NonNegative(distanceMeters, nameof(distanceMeters));
        if (averageSpeedKmh <= 0)
            throw new ArgumentOutOfRangeException(nameof(averageSpeedKmh));
        return checked((int)decimal.Ceiling(distanceMeters * 60m / (averageSpeedKmh * 1000m)));
    }

    /// <summary>
    /// BR-058: التأخر بعد الحد الأعلى + مهلة، وليس عند المساواة.
    /// استدعها على نافذة محفوظة قبل إعادة الحساب كي لا يتحرك حد التأخر باستمرار.
    /// إنشاء الحادثة والإشعار مرة واحدة مسؤولية معاملة التخزين، لا هذا الكلاس.
    /// </summary>
    public static bool IsLate(DateTime nowUtc, DateTime persistedMaxAtUtc,
        int lateGraceMinutes, bool terminal)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        ValidateUtc(persistedMaxAtUtc, nameof(persistedMaxAtUtc));
        NonNegative(lateGraceMinutes, nameof(lateGraceMinutes));
        return !terminal && nowUtc > persistedMaxAtUtc.AddMinutes(lateGraceMinutes);
    }

    private static Estimate Build(DateTime min, int travel, Policy policy)
    {
        var max = min.AddMinutes(policy.WindowMinutes);
        return new Estimate(min, max, max.AddMinutes(policy.LateGraceMinutes), travel);
    }

    private static DateTime Later(DateTime a, DateTime b) => a >= b ? a : b;

    private static void Validate(Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.AverageSpeedKmh <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy.AverageSpeedKmh));
        NonNegative(policy.PickupBufferMinutes, nameof(policy.PickupBufferMinutes));
        NonNegative(policy.WindowMinutes, nameof(policy.WindowMinutes));
        NonNegative(policy.LateGraceMinutes, nameof(policy.LateGraceMinutes));
    }

    private static void NonNegative(int value, string name)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("An explicit UTC DateTime is required.", name);
    }
}
