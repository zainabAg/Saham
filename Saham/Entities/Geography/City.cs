using System;
using NetTopologySuite.Geometries;
using Saham.Entities.Shared;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Geography;

/* A city the platform operates in (SRS table cities; Karbala now, Najaf and Hilla later — S-18).
 * ServiceArea is the delivery polygon drawn by the owner (DER-ADR-07); while it is empty,
 * every point is accepted and only the distance limit of the pricing plan applies. */
public class City : AuditedAggregateRoot<Guid>
{
    public const string DefaultTimeZone = "Asia/Baghdad";

    public I18nText Name { get; private set; }

    public string TimeZone { get; private set; }

    public Point Center { get; private set; }

    public MultiPolygon? ServiceArea { get; private set; }

    public bool IsActive { get; private set; }

    protected City()
    {
        Name = null!;
        TimeZone = DefaultTimeZone;
        Center = null!;
    }

    public City(Guid id, I18nText name, Point center, string timeZone = DefaultTimeZone)
        : base(id)
    {
        Name = name;
        Center = center;
        TimeZone = Check.NotNullOrWhiteSpace(timeZone, nameof(timeZone), 64);
        IsActive = true;
    }

    public void Update(I18nText name, Point center, bool isActive)
    {
        Name = name;
        Center = center;
        IsActive = isActive;
    }

    public void SetServiceArea(MultiPolygon? area) => ServiceArea = area;

    public bool Serves(Point point) => ServiceArea == null || ServiceArea.Covers(point);

    public TimeZoneInfo GetTimeZoneInfo() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /* Opening hours are local times of the city; everything is stored in UTC (ASM-02). */
    public DateTime ToLocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), GetTimeZoneInfo());

    public DateTime ToUtc(DateTime localTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), GetTimeZoneInfo());
}
