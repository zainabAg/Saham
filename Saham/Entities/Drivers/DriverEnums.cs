namespace Saham.Entities.Drivers;

/* BR-051 / SOD-07: ops review the documents, the owner gives the final approval. */
public enum DriverReviewStatus
{
    PendingReview = 0,
    DocsAccepted = 1,
    Approved = 2,
    Rejected = 3,
    Suspended = 4
}

public enum DriverAvailability
{
    Offline = 0,
    Available = 1
}

/* BR-066 / BR-067 (phase 7): no cash orders while set. */
public enum DriverCashBlock
{
    Overdue = 0,
    Cap = 1
}

/* REQ-DRV-01: the three mandatory documents. */
public enum DriverDocumentType
{
    NationalId = 0,
    DrivingLicense = 1,
    BikePhoto = 2
}

public enum DriverDocumentStatus
{
    Submitted = 0,
    Accepted = 1,
    Rejected = 2
}
