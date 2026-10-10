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
}
