namespace Saham.Entities.Dispatch;

public enum OfferStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
    Cancelled = 4
}

/* delivery_assignments.status (SRS 10.5). Active = Assigned or PickedUp. */
public enum AssignmentStatus
{
    Assigned = 0,
    PickedUp = 1,
    Delivered = 2,
    Failed = 3,
    Released = 4
}

public enum AssignedByType
{
    System = 0,
    Staff = 1
}
