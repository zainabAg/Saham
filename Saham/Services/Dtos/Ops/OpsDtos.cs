using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Drivers;
using Saham.Entities.Notifications;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Ops;

// ------------------------------------------------------------------ incidents (DER-OPS-02)

public class IncidentDto : EntityDto<Guid>
{
    public IncidentType IncidentType { get; set; }

    public IncidentSeverity Severity { get; set; }

    public IncidentStatus Status { get; set; }

    public Guid? OrderId { get; set; }

    public long? OrderNumber { get; set; }

    public Guid? DriverId { get; set; }

    public string? DriverName { get; set; }

    public Guid? RestaurantId { get; set; }

    public string? RestaurantName { get; set; }

    public Guid? AssignedTo { get; set; }

    public string? AssignedToName { get; set; }

    public DateTime OpenedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    public string? Resolution { get; set; }

    public string? Note { get; set; }
}

public class GetIncidentsInput : PagedResultRequestDto
{
    public bool OpenOnly { get; set; } = true;

    public IncidentStatus? Status { get; set; }

    public IncidentType? IncidentType { get; set; }

    public Guid? AssignedTo { get; set; }

    /* Only incidents nobody has taken yet. */
    public bool UnassignedOnly { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? DriverId { get; set; }

    public Guid? RestaurantId { get; set; }
}

public class OpenIncidentDto
{
    public IncidentType IncidentType { get; set; } = IncidentType.Other;

    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;

    public Guid? OrderId { get; set; }

    public Guid? DriverId { get; set; }

    public Guid? RestaurantId { get; set; }

    [Required]
    [StringLength(Incident.NoteMaxLength)]
    public string Note { get; set; } = string.Empty;
}

public class AssignIncidentDto
{
    /* Empty: the current user takes it. */
    public Guid? UserId { get; set; }
}

public class IncidentNoteDto
{
    [StringLength(Incident.NoteMaxLength)]
    public string? Note { get; set; }
}

public class ResolveIncidentDto
{
    [Required]
    [StringLength(Incident.ResolutionMaxLength)]
    public string Resolution { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ live orders board (REQ-OPS-01)

public static class AttentionReasons
{
    public const string Late = "LATE";
    public const string OnHold = "ON_HOLD";
    public const string NoDriver = "NO_DRIVER";
    public const string WaitingForRestaurant = "WAITING_FOR_RESTAURANT";
    public const string DeliveryCodeLocked = "DELIVERY_CODE_LOCKED";
    public const string NotificationFailed = "NOTIFICATION_FAILED";
    public const string OpenIncident = "OPEN_INCIDENT";
}

public class LiveOrderDto : EntityDto<Guid>
{
    public long Number { get; set; }

    public OrderStatus Status { get; set; }

    public OrderHoldReason? HoldReason { get; set; }

    public DispatchStatus DispatchStatus { get; set; }

    public Guid RestaurantId { get; set; }

    public string RestaurantName { get; set; } = string.Empty;

    public Guid? DriverId { get; set; }

    public string? DriverName { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public long AmountDue { get; set; }

    public DateTime? PlacedAt { get; set; }

    /* Minutes since the order reached the restaurant. */
    public int AgeMinutes { get; set; }

    public DateTime EtaMaxAt { get; set; }

    /* Minutes left until the latest promised time; negative when past it. */
    public int RemainingMinutes { get; set; }

    /* BR-058: past the promised time by more than the late threshold. */
    public bool IsLate { get; set; }

    public bool NeedsAttention { get; set; }

    public List<string> AttentionReasons { get; set; } = new();

    public List<IncidentType> OpenIncidents { get; set; } = new();
}

public class GetLiveOrdersInput
{
    public Guid? RestaurantId { get; set; }

    public bool AttentionOnly { get; set; }
}

// ------------------------------------------------------------------ drivers map (REQ-OPS-02)

public enum DriverMapState
{
    Available = 0,
    OnTask = 1,
    LocationStale = 2,
    Offline = 3
}

public class DriverMapItemDto : EntityDto<Guid>
{
    public string? Name { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public DriverMapState State { get; set; }

    public DriverAvailability Availability { get; set; }

    public int ActiveOrderCount { get; set; }

    public List<long> ActiveOrderNumbers { get; set; } = new();

    public DriverCashBlock? CashBlocked { get; set; }

    public LocationDto? Location { get; set; }

    public DateTime? LastLocationAt { get; set; }

    public int? LocationAgeSeconds { get; set; }
}

public class DriversMapDto
{
    public List<DriverMapItemDto> Drivers { get; set; } = new();

    public int Available { get; set; }

    public int OnTask { get; set; }

    public int LocationStale { get; set; }

    public int Offline { get; set; }
}

// ------------------------------------------------------------------ restaurants (DER-OPS-01)

public enum RestaurantBoardState
{
    Open = 0,
    TemporarilyClosed = 1,
    OutsideHours = 2,
    TabletOffline = 3,
    Inactive = 4
}

public class RestaurantStatusDto : EntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    public RestaurantBoardState State { get; set; }

    public RestaurantStatus Status { get; set; }

    public bool IsPaused { get; set; }

    public string? PausedReason { get; set; }

    public DateTime? PausedUntil { get; set; }

    public bool IsOpenNow { get; set; }

    public DateTime? TabletLastSeenAt { get; set; }

    public bool TabletOnline { get; set; }

    /* Orders waiting for the restaurant's answer. */
    public int PendingResponseCount { get; set; }

    public DateTime? OldestPendingSince { get; set; }

    public int ActiveOrderCount { get; set; }
}

// ------------------------------------------------------------------ notifications (I-6)

public class NotificationDto : EntityDto<Guid>
{
    public string EventKey { get; set; } = string.Empty;

    public NotificationChannel Channel { get; set; }

    public Guid? UserId { get; set; }

    public string? Phone { get; set; }

    public string Template { get; set; } = string.Empty;

    public Guid? OrderId { get; set; }

    public NotificationStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    public string? LastError { get; set; }

    public DateTime CreationTime { get; set; }
}

public class GetNotificationsInput : PagedResultRequestDto
{
    /* Default: the ones that need a look (failed and given up). */
    public bool FailedOnly { get; set; } = true;

    public NotificationStatus? Status { get; set; }

    public NotificationChannel? Channel { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? UserId { get; set; }
}
