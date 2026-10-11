using Saham.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace Saham.Settings;

/* Default values come from the SRS assumption log (3.3) and the client decisions (Appendix D). */
public class SahamSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // Auth (DEC-01 / ASM-13)
        Add(context, SahamSettings.Auth.OtpLength, "4");
        Add(context, SahamSettings.Auth.OtpLifetimeSeconds, "300");
        Add(context, SahamSettings.Auth.OtpMaxAttempts, "5");
        Add(context, SahamSettings.Auth.OtpResendSeconds, "60");

        // Orders
        Add(context, SahamSettings.Orders.RestaurantAlertMinutes, "2");        // ASM-10
        Add(context, SahamSettings.Orders.RestaurantAutoCancelMinutes, "10");  // ASM-10
        Add(context, SahamSettings.Orders.ItemDecisionMinutes, "5");           // DEC-10
        Add(context, SahamSettings.Orders.MinPrepMinutes, "5");
        Add(context, SahamSettings.Orders.MaxPrepMinutes, "120");
        Add(context, SahamSettings.Orders.LateThresholdMinutes, "10");         // ASM-21
        Add(context, SahamSettings.Orders.QuoteLifetimeMinutes, "10");
        Add(context, SahamSettings.Orders.WalletPaymentTimeoutMinutes, "15");  // ASM-17
        Add(context, SahamSettings.Orders.AddressMinDescriptionLength, "5");   // ASM-22
        Add(context, SahamSettings.Orders.ScheduleMinLeadMinutes, "60");       // BR-020 / REC-13
        Add(context, SahamSettings.Orders.ScheduleMaxDays, "3");               // DEC-02
        Add(context, SahamSettings.Orders.AverageSpeedKmh, "20");              // ETA estimate (motorbike in town)
        Add(context, SahamSettings.Orders.PickupBufferMinutes, "5");           // ETA: driver reaching the restaurant
        Add(context, SahamSettings.Orders.ScheduleReleaseMarginMinutes, "10"); // BR-021
        Add(context, SahamSettings.Orders.ItemDecisionOpsMinutes, "5");        // BR-047 / UC-12 E1: extra minutes for the ops call

        // Dispatch
        Add(context, SahamSettings.Dispatch.OfferTimeoutSeconds, "30");        // S-08
        Add(context, SahamSettings.Dispatch.StartBeforeReadyMinutes, "10");    // ASM-11
        Add(context, SahamSettings.Dispatch.RetryIntervalSeconds, "30");       // EX-04
        Add(context, SahamSettings.Dispatch.NoDriverIncidentMinutes, "5");     // EX-04
        Add(context, SahamSettings.Dispatch.MaxActiveOrdersPerDriver, "2");    // S-08

        // Drivers
        Add(context, SahamSettings.Drivers.LocationFreshSeconds, "120");       // ASM-12
        Add(context, SahamSettings.Drivers.StaleMinutes, "5");                 // ASM-23
        Add(context, SahamSettings.Drivers.ArrivedAtRestaurantRadiusMeters, "200"); // ASM-37
        Add(context, SahamSettings.Drivers.DeliveryGeofenceMeters, "300");     // ASM-20

        // Delivery
        Add(context, SahamSettings.Delivery.OtpMaxAttempts, "5");              // ASM-13
        Add(context, SahamSettings.Delivery.NoShowWaitMinutes, "10");          // DEC-06
        Add(context, SahamSettings.Delivery.NoShowCashBanThreshold, "2");      // DEC-06

        // Cash (amounts in IQD)
        Add(context, SahamSettings.Cash.DriverCap, "300000");                  // DEC-09
        Add(context, SahamSettings.Cash.OverdueHour, "12");                    // DEC-09
        Add(context, SahamSettings.Cash.FloatAmount, "25000");                 // REC-04
        Add(context, SahamSettings.Cash.BusinessDayEndHour, "4");              // ASM-09

        // Payments (UC-05, UC-22)
        Add(context, SahamSettings.Payments.WalletEnabled, "true");            // EXT-01: FakeWalletProvider until signed
        Add(context, SahamSettings.Payments.RefundRetrySeconds, "60,300,900,3600,10800"); // UC-22 E1: 1, 5, 15, 60, 180 min

        // In-app calls (TD-03)
        Add(context, SahamSettings.Calls.RingTimeoutSeconds, "45");

        // Settlements (DEC-09, BR-040, BR-068)
        Add(context, SahamSettings.Settlements.DriverPayoutDay, "Thursday");   // earnings up to Wednesday
        Add(context, SahamSettings.Settlements.RestaurantWeekStartDay, "Saturday"); // week Saturday → Friday

        // Restaurants
        Add(context, SahamSettings.Restaurants.TabletOfflineSeconds, "120");   // DER-OPS-01: tablet pings every 60 s

        // Notifications (REQ-NTF-02: 3 attempts with growing gaps: 30 s, 60 s)
        Add(context, SahamSettings.Notifications.MaxAttempts, "3");
        Add(context, SahamSettings.Notifications.RetryBaseSeconds, "30");

        // Support
        Add(context, SahamSettings.Support.OpsCompensationLimit, "5000");      // DEC-11

        // System
        Add(context, SahamSettings.System.RecoveryMode, "false");              // DER-SYS-01
        Add(context, SahamSettings.System.RecoveryModeMinutes, "15");
        Add(context, SahamSettings.System.IdempotencyRetentionDays, "7");
    }

    private static void Add(ISettingDefinitionContext context, string name, string defaultValue)
    {
        context.Add(new SettingDefinition(
            name,
            defaultValue,
            LocalizableString.Create<SahamResource>("Setting:" + name),
            isVisibleToClients: false));
    }
}
