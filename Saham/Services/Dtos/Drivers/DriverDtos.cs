using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Drivers;

// ------------------------------------------------------------------ driver app

public class RegisterDriverDto
{
    [Required]
    [StringLength(SahamConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /* Defaults to the first active city. */
    public Guid? CityId { get; set; }

    [StringLength(Driver.VehiclePlateMaxLength)]
    public string? VehiclePlate { get; set; }

    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string? EmergencyContactPhone { get; set; }
}

public class DriverDocumentDto : EntityDto<Guid>
{
    public DriverDocumentType DocType { get; set; }

    public DriverDocumentStatus Status { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? ReviewNote { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? ReviewedAt { get; set; }
}

public class DriverProfileDto : EntityDto<Guid>
{
    public string? Name { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public DriverReviewStatus ReviewStatus { get; set; }

    public string? ReviewNote { get; set; }

    public DriverAvailability Availability { get; set; }

    public int ActiveOrderCount { get; set; }

    public DriverCashBlock? CashBlocked { get; set; }

    public string? VehiclePlate { get; set; }

    public long FloatAmount { get; set; }

    public DateTime? LastLocationAt { get; set; }

    /* Latest document of each type (rejected ones included, with the reason). */
    public List<DriverDocumentDto> Documents { get; set; } = new();
}

public class SetAvailabilityDto
{
    public bool IsAvailable { get; set; }
}

public class LocationPointDto
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Range(0, 100_000)]
    public double? AccuracyM { get; set; }

    /* Device time of the reading (UTC). */
    public DateTime RecordedAt { get; set; }

    public Guid? OrderId { get; set; }
}

/* A batch of points: live every 15–60 s, or the offline queue after a network gap (EX-01). */
public class PostLocationsDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(500)]
    public List<LocationPointDto> Points { get; set; } = new();
}

public class PostLocationsResultDto
{
    public int Accepted { get; set; }

    public DateTime? LastLocationAt { get; set; }
}

/* What a driver sees before accepting (REQ-DRV-04): where to pick up, how far, how much he earns,
 * and — for cash — how much to collect and the change to carry (DER-CUS-05).
 * No customer name, phone or exact address before acceptance. */
public class DriverOfferDto : EntityDto<Guid>
{
    public Guid OrderId { get; set; }

    public long OrderNumber { get; set; }

    public DateTime ExpiresAt { get; set; }

    /* For the countdown: the server clock, not the phone's (EX-05). */
    public DateTime ServerNow { get; set; }

    public string RestaurantName { get; set; } = string.Empty;

    public string RestaurantAddress { get; set; } = string.Empty;

    public LocationDto RestaurantLocation { get; set; } = new();

    public int DistanceToRestaurantM { get; set; }

    public int DeliveryDistanceM { get; set; }

    public long DriverPay { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public long AmountToCollect { get; set; }

    public long? CashTendered { get; set; }

    public long? ChangeToGive { get; set; }
}

/* The task after acceptance (BR-036): first name, description and pin — never a phone number. */
public class DriverTaskDto
{
    public Guid AssignmentId { get; set; }

    public Guid OrderId { get; set; }

    public long OrderNumber { get; set; }

    public OrderStatus OrderStatus { get; set; }

    public Saham.Entities.Dispatch.AssignmentStatus AssignmentStatus { get; set; }

    public DateTime? ArrivedAtRestaurantAt { get; set; }

    public DateTime? PickedUpAt { get; set; }

    public DateTime? ArrivedAtCustomerAt { get; set; }

    public bool IsBatch { get; set; }

    public long DriverPay { get; set; }

    public string RestaurantName { get; set; } = string.Empty;

    public string RestaurantAddress { get; set; } = string.Empty;

    public LocationDto RestaurantLocation { get; set; } = new();

    public DateTime? EstimatedReadyAt { get; set; }

    public string RecipientFirstName { get; set; } = string.Empty;

    public string DeliveryDescription { get; set; } = string.Empty;

    public LocationDto DeliveryLocation { get; set; } = new();

    public PaymentMethod PaymentMethod { get; set; }

    public long AmountToCollect { get; set; }

    public long? ChangeToGive { get; set; }
}

// ------------------------------------------------------------------ ops / owner

public class GetDriversInput : PagedResultRequestDto
{
    public DriverReviewStatus? ReviewStatus { get; set; }

    public DriverAvailability? Availability { get; set; }

    public string? Filter { get; set; }
}

public class DriverAdminDto : EntityDto<Guid>
{
    public string? Name { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public DriverReviewStatus ReviewStatus { get; set; }

    public string? ReviewNote { get; set; }

    public DriverAvailability Availability { get; set; }

    public int ActiveOrderCount { get; set; }

    public DriverCashBlock? CashBlocked { get; set; }

    public string? VehiclePlate { get; set; }

    public string? EmergencyContactPhone { get; set; }

    public LocationDto? LastLocation { get; set; }

    public DateTime? LastLocationAt { get; set; }

    public DateTime CreationTime { get; set; }

    public List<DriverDocumentDto> Documents { get; set; } = new();
}

public class ReviewDocumentDto
{
    public bool Accept { get; set; }

    [StringLength(512)]
    public string? Note { get; set; }
}

public class ReasonDto
{
    [Required]
    [StringLength(512)]
    public string Reason { get; set; } = string.Empty;
}

public class AssignDriverDto
{
    [Required]
    public Guid DriverId { get; set; }

    [Required]
    [StringLength(512)]
    public string Reason { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ driver task actions

/* Every driver action carries the time it happened on the phone, for actions replayed from the
 * offline queue (EX-01). Sent with an Idempotency-Key, so a replay never runs twice. */
public class DriverActionDto
{
    public DateTime? ClientOccurredAt { get; set; }
}

public class DeliverDto : DriverActionDto
{
    [Required]
    [StringLength(8)]
    public string Code { get; set; } = string.Empty;

    /* EX-14: change the driver could not give back; becomes the customer's credit. */
    [Range(0, long.MaxValue)]
    public long ChangeShortfall { get; set; }
}

public class DeliverResultDto
{
    public bool Delivered { get; set; }

    /* The code is locked after too many wrong attempts; ops take over. */
    public bool Locked { get; set; }

    public int AttemptsLeft { get; set; }

    public DriverTaskDto? Task { get; set; }
}

