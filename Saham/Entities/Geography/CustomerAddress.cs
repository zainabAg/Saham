using System;
using NetTopologySuite.Geometries;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Geography;

/* A saved delivery address (SRS table customer_addresses, REQ-ADR-01..04):
 * a pin on the map + a landmark description, no street names or numbers (S-03).
 * Deleting is archiving (soft delete); orders copy the address when created (DER-ADR-01),
 * so editing it never changes an existing order. */
public class CustomerAddress : FullAuditedAggregateRoot<Guid>
{
    public const int DescriptionMaxLength = 512;
    public const int CustomLabelMaxLength = 64;

    public Guid UserId { get; private set; }

    public Guid CityId { get; private set; }

    public AddressLabel Label { get; private set; }

    public string? CustomLabel { get; private set; }

    public Point Location { get; private set; }

    public string Description { get; private set; }

    public string? RecipientName { get; private set; }

    public string? RecipientPhone { get; private set; }

    protected CustomerAddress()
    {
        Location = null!;
        Description = null!;
    }

    internal CustomerAddress(Guid id, Guid userId, Guid cityId)
        : base(id)
    {
        UserId = userId;
        CityId = cityId;
        Location = null!;
        Description = null!;
    }

    /* Validation (description length, service area) lives in AddressManager. */
    internal void Set(
        Guid cityId,
        AddressLabel label,
        string? customLabel,
        Point location,
        string description,
        string? recipientName,
        string? recipientPhone)
    {
        CityId = cityId;
        Label = label;
        CustomLabel = label == AddressLabel.Other ? customLabel?.Trim() : null;
        Location = location;
        Description = description.Trim();
        RecipientName = recipientName.IsNullOrWhiteSpace() ? null : recipientName!.Trim();
        RecipientPhone = recipientPhone;
    }
}
