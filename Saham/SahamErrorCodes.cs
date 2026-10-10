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
}
