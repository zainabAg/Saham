namespace Saham.Settings;

/* Every ASM-nn value in the SRS is a setting, never a constant in code (SRS 3.3). */
public static class SahamSettings
{
    private const string Prefix = "Saham";

    public static class Auth
    {
        public const string OtpLength = Prefix + ".Auth.OtpLength";
        public const string OtpLifetimeSeconds = Prefix + ".Auth.OtpLifetimeSeconds";
        public const string OtpMaxAttempts = Prefix + ".Auth.OtpMaxAttempts";
        public const string OtpResendSeconds = Prefix + ".Auth.OtpResendSeconds";
    }

    public static class Orders
    {
        public const string RestaurantAlertMinutes = Prefix + ".Orders.RestaurantAlertMinutes";
        public const string RestaurantAutoCancelMinutes = Prefix + ".Orders.RestaurantAutoCancelMinutes";
        public const string ItemDecisionMinutes = Prefix + ".Orders.ItemDecisionMinutes";
        public const string MinPrepMinutes = Prefix + ".Orders.MinPrepMinutes";
        public const string MaxPrepMinutes = Prefix + ".Orders.MaxPrepMinutes";
        public const string LateThresholdMinutes = Prefix + ".Orders.LateThresholdMinutes";
        public const string QuoteLifetimeMinutes = Prefix + ".Orders.QuoteLifetimeMinutes";
        public const string WalletPaymentTimeoutMinutes = Prefix + ".Orders.WalletPaymentTimeoutMinutes";
        public const string AddressMinDescriptionLength = Prefix + ".Orders.AddressMinDescriptionLength";
        public const string ScheduleMinLeadMinutes = Prefix + ".Orders.ScheduleMinLeadMinutes";
        public const string ScheduleMaxDays = Prefix + ".Orders.ScheduleMaxDays";
        public const string AverageSpeedKmh = Prefix + ".Orders.AverageSpeedKmh";
        public const string PickupBufferMinutes = Prefix + ".Orders.PickupBufferMinutes";
        public const string ScheduleReleaseMarginMinutes = Prefix + ".Orders.ScheduleReleaseMarginMinutes";
    }

    public static class Dispatch
    {
        public const string OfferTimeoutSeconds = Prefix + ".Dispatch.OfferTimeoutSeconds";
        public const string StartBeforeReadyMinutes = Prefix + ".Dispatch.StartBeforeReadyMinutes";
        public const string RetryIntervalSeconds = Prefix + ".Dispatch.RetryIntervalSeconds";
        public const string NoDriverIncidentMinutes = Prefix + ".Dispatch.NoDriverIncidentMinutes";
        public const string MaxActiveOrdersPerDriver = Prefix + ".Dispatch.MaxActiveOrdersPerDriver";
    }

    public static class Drivers
    {
        public const string LocationFreshSeconds = Prefix + ".Drivers.LocationFreshSeconds";
        public const string StaleMinutes = Prefix + ".Drivers.StaleMinutes";
        public const string ArrivedAtRestaurantRadiusMeters = Prefix + ".Drivers.ArrivedAtRestaurantRadiusMeters";
        public const string DeliveryGeofenceMeters = Prefix + ".Drivers.DeliveryGeofenceMeters";
    }

    public static class Delivery
    {
        public const string OtpMaxAttempts = Prefix + ".Delivery.OtpMaxAttempts";
        public const string NoShowWaitMinutes = Prefix + ".Delivery.NoShowWaitMinutes";
        public const string NoShowCashBanThreshold = Prefix + ".Delivery.NoShowCashBanThreshold";
    }

    public static class Cash
    {
        public const string DriverCap = Prefix + ".Cash.DriverCap";
        public const string OverdueHour = Prefix + ".Cash.OverdueHour";
        public const string FloatAmount = Prefix + ".Cash.FloatAmount";
        public const string BusinessDayEndHour = Prefix + ".Cash.BusinessDayEndHour";
    }

    public static class Restaurants
    {
        public const string TabletOfflineSeconds = Prefix + ".Restaurants.TabletOfflineSeconds";
    }

    public static class Notifications
    {
        public const string MaxAttempts = Prefix + ".Notifications.MaxAttempts";
        public const string RetryBaseSeconds = Prefix + ".Notifications.RetryBaseSeconds";
    }

    public static class Support
    {
        public const string OpsCompensationLimit = Prefix + ".Support.OpsCompensationLimit";
    }

    public static class System
    {
        public const string RecoveryMode = Prefix + ".System.RecoveryMode";
        public const string RecoveryModeMinutes = Prefix + ".System.RecoveryModeMinutes";
        public const string IdempotencyRetentionDays = Prefix + ".System.IdempotencyRetentionDays";
    }
}
