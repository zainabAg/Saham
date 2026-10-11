namespace Saham;

/* Business error codes (SRS sections 10.7, 11.3).
 * Each code is localized under the same key in Localization/Saham/*.json
 * and mapped to an HTTP status in SahamModule.ConfigureExceptionHandling.
 */
public static class SahamErrorCodes
{
    private const string Prefix = "Saham:";

    // General
    public const string InvalidStateTransition = Prefix + "InvalidStateTransition";
    public const string IdempotencyKeyRequired = Prefix + "IdempotencyKeyRequired";
    public const string IdempotencyConflict = Prefix + "IdempotencyConflict";
    public const string RequestInProgress = Prefix + "RequestInProgress";

    // Identity & access
    public const string InvalidPhoneNumber = Prefix + "InvalidPhoneNumber";
    public const string OtpTooManyRequests = Prefix + "OtpTooManyRequests";
    public const string OtpInvalid = Prefix + "OtpInvalid";
    public const string OtpExpired = Prefix + "OtpExpired";
    public const string OtpLocked = Prefix + "OtpLocked";
    public const string UserDisabled = Prefix + "UserDisabled";
    public const string PhoneAlreadyRegistered = Prefix + "PhoneAlreadyRegistered";
    public const string CannotChangeOwnRoles = Prefix + "CannotChangeOwnRoles";
    public const string LastOwnerCannotBeRemoved = Prefix + "LastOwnerCannotBeRemoved";
    public const string NotRestaurantStaff = Prefix + "NotRestaurantStaff";
    public const string InvalidRestaurantStaffRole = Prefix + "InvalidRestaurantStaffRole";
    public const string InvalidStaffRole = Prefix + "InvalidStaffRole";

    // Catalog & geography
    public const string InvalidAmount = Prefix + "InvalidAmount";
    public const string InvalidLocation = Prefix + "InvalidLocation";
    public const string InvalidServiceArea = Prefix + "InvalidServiceArea";
    public const string RestaurantIdRequired = Prefix + "RestaurantIdRequired";
    public const string InvalidCommissionRate = Prefix + "InvalidCommissionRate";
    public const string InvalidPrepMinutes = Prefix + "InvalidPrepMinutes";
    public const string InvalidPauseUntil = Prefix + "InvalidPauseUntil";
    public const string PauseReasonRequired = Prefix + "PauseReasonRequired";
    public const string InvalidOpeningHours = Prefix + "InvalidOpeningHours";
    public const string MenuCategoryNotInRestaurant = Prefix + "MenuCategoryNotInRestaurant";
    public const string AddressDescriptionTooShort = Prefix + "AddressDescriptionTooShort";
    public const string AddressOutsideServiceArea = Prefix + "AddressOutsideServiceArea";

    // Pricing & quotes
    public const string InvalidPricingPlan = Prefix + "InvalidPricingPlan";
    public const string InvalidPricingTiers = Prefix + "InvalidPricingTiers";
    public const string PricingPlanNotEditable = Prefix + "PricingPlanNotEditable";
    public const string NoActivePricingPlan = Prefix + "NoActivePricingPlan";
    public const string EmptyCart = Prefix + "EmptyCart";
    public const string InvalidQuantity = Prefix + "InvalidQuantity";
    public const string ItemUnavailable = Prefix + "ItemUnavailable";
    public const string RestaurantClosed = Prefix + "RestaurantClosed";
    public const string BelowMinOrder = Prefix + "BelowMinOrder";
    public const string AddressOutOfRange = Prefix + "AddressOutOfRange";
    public const string InvalidScheduleTime = Prefix + "InvalidScheduleTime";
    public const string QuoteExpired = Prefix + "QuoteExpired";
    public const string QuoteAlreadyUsed = Prefix + "QuoteAlreadyUsed";

    // Orders
    public const string PriceChanged = Prefix + "PriceChanged";
    public const string WalletNotAvailable = Prefix + "WalletNotAvailable";
    public const string CashNotAllowed = Prefix + "CashNotAllowed";
    public const string CashTenderedTooLow = Prefix + "CashTenderedTooLow";
    public const string CustomerCannotCancel = Prefix + "CustomerCannotCancel";
    public const string CancelNoteRequired = Prefix + "CancelNoteRequired";
    public const string OrderOnHold = Prefix + "OrderOnHold";
    public const string NoItemDecisionPending = Prefix + "NoItemDecisionPending";
    public const string OrderItemNotFound = Prefix + "OrderItemNotFound";

    // Drivers & dispatch
    public const string ReasonRequired = Prefix + "ReasonRequired";
    public const string NotADriver = Prefix + "NotADriver";
    public const string DriverAlreadyRegistered = Prefix + "DriverAlreadyRegistered";
    public const string DriverNotApproved = Prefix + "DriverNotApproved";
    public const string DriverHasActiveOrders = Prefix + "DriverHasActiveOrders";
    public const string DriverNotAvailable = Prefix + "DriverNotAvailable";
    public const string DriverCashBlocked = Prefix + "DriverCashBlocked";
    public const string DriverAtCapacity = Prefix + "DriverAtCapacity";
    public const string DriverDocumentsIncomplete = Prefix + "DriverDocumentsIncomplete";
    public const string DocumentInvalidType = Prefix + "DocumentInvalidType";
    public const string DocumentTooLarge = Prefix + "DocumentTooLarge";
    public const string DocumentAlreadyAccepted = Prefix + "DocumentAlreadyAccepted";
    public const string InvalidDriverReviewState = Prefix + "InvalidDriverReviewState";
    public const string OfferExpired = Prefix + "OfferExpired";
    public const string OfferNotPending = Prefix + "OfferNotPending";
    public const string OrderAlreadyAssigned = Prefix + "OrderAlreadyAssigned";
    public const string OrderNotDispatchable = Prefix + "OrderNotDispatchable";
    public const string NoActiveAssignment = Prefix + "NoActiveAssignment";
    public const string AssignmentAlreadyPickedUp = Prefix + "AssignmentAlreadyPickedUp";

    // Pickup & delivery
    public const string AssignmentReleased = Prefix + "AssignmentReleased";
    public const string TooFarFromRestaurant = Prefix + "TooFarFromRestaurant";
    public const string DeliveryCodeLocked = Prefix + "DeliveryCodeLocked";
    public const string NoShowTooEarly = Prefix + "NoShowTooEarly";
    public const string NoDriverLocation = Prefix + "NoDriverLocation";

    // Money
    public const string UnbalancedEntry = Prefix + "UnbalancedEntry";
    public const string InvalidCashAmount = Prefix + "InvalidCashAmount";
    public const string ChangeShortfallTooLarge = Prefix + "ChangeShortfallTooLarge";
    public const string ContactAttemptRequired = Prefix + "ContactAttemptRequired";
    public const string FloatReturnTooLarge = Prefix + "FloatReturnTooLarge";

    // Phase 8 - ops board, incidents, notifications
    public const string IncidentAlreadyResolved = Prefix + "IncidentAlreadyResolved";
    public const string IncidentSubjectRequired = Prefix + "IncidentSubjectRequired";
    public const string NotificationNotDead = Prefix + "NotificationNotDead";

    // Phase 9 - payments, refunds, coupons, settlements, substitutes
    public const string RefundExceedsPayment = Prefix + "RefundExceedsPayment";
    public const string InvalidRefundStatus = Prefix + "InvalidRefundStatus";
    public const string CouponNotFound = Prefix + "CouponNotFound";
    public const string CouponNotValidNow = Prefix + "CouponNotValidNow";
    public const string CouponNotForRestaurant = Prefix + "CouponNotForRestaurant";
    public const string CouponBelowMinSubtotal = Prefix + "CouponBelowMinSubtotal";
    public const string CouponUsageLimitReached = Prefix + "CouponUsageLimitReached";
    public const string CouponCodeTaken = Prefix + "CouponCodeTaken";
    public const string CouponAlreadyUsed = Prefix + "CouponAlreadyUsed";
    public const string CouponRestaurantRequired = Prefix + "CouponRestaurantRequired";
    public const string InvalidCouponTerms = Prefix + "InvalidCouponTerms";
    public const string InvalidCouponStatus = Prefix + "InvalidCouponStatus";
    public const string InvalidSubstitute = Prefix + "InvalidSubstitute";
    public const string NoSubstituteProposed = Prefix + "NoSubstituteProposed";
    public const string LateCancelOnlyAfterAcceptance = Prefix + "LateCancelOnlyAfterAcceptance";
    public const string InvalidSettlementStatus = Prefix + "InvalidSettlementStatus";
    public const string SettlementPeriodNotClosed = Prefix + "SettlementPeriodNotClosed";
    public const string InvalidSettlementPeriod = Prefix + "InvalidSettlementPeriod";
    public const string UnknownReport = Prefix + "UnknownReport";
}
