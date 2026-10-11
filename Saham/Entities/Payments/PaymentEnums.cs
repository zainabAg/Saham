namespace Saham.Entities.Payments;

/* SRS payments.status. Initiated = session being opened, Pending = waiting for the customer to pay. */
public enum PaymentStatus
{
    Initiated = 0,
    Pending = 1,
    Captured = 2,
    Failed = 3,
    Expired = 4
}

/* SRS refunds.status (UC-22). */
public enum RefundStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    FailedRetryable = 3,
    ManualReview = 4,
    ResolvedManually = 5
}

/* SRS refunds.reason. */
public enum RefundReason
{
    Cancellation = 0,
    Modification = 1,
    LateCapture = 2,
    Complaint = 3
}

/* How the accountant returned the money outside the system (J-REFUND-MANUAL). */
public enum ManualPaymentChannel
{
    Bank = 0,
    Cash = 1
}
