namespace Saham.Permissions;

/* Permission codes follow the RBAC matrix in SRS section 5.3.
 * Role grants live in Data/Seeding/RolesAndPermissionsDataSeedContributor. */
public static class SahamPermissions
{
    public const string GroupName = "Saham";


    public static class Restaurants
    {
        public const string Default = GroupName + ".Restaurants";
        public const string Browse = Default + ".Browse";
        public const string Onboard = Default + ".Onboard";
        public const string HoursMinOrder = Default + ".HoursMinOrder";
        public const string Pause = Default + ".Pause";
        public const string UsersManage = Default + ".UsersManage";
        public const string CouponRequest = Default + ".CouponRequest";
    }

    public static class Menu
    {
        public const string Default = GroupName + ".Menu";
        public const string Manage = Default + ".Manage";
        public const string ToggleAvailability = Default + ".ToggleAvailability";
    }

    public static class Orders
    {
        public const string Default = GroupName + ".Orders";
        public const string Create = Default + ".Create";
        public const string View = Default + ".View";
        public const string CancelCustomer = Default + ".CancelCustomer";
        public const string ItemDecision = Default + ".ItemDecision";
        public const string AcceptReject = Default + ".AcceptReject";
        public const string MarkReady = Default + ".MarkReady";
        public const string CancelRestaurant = Default + ".CancelRestaurant";
        public const string ItemUnavailable = Default + ".ItemUnavailable";
        public const string HandoverToDriver = Default + ".HandoverToDriver";
        public const string CancelOps = Default + ".CancelOps";
        public const string Modify = Default + ".Modify";
        public const string ForceDeliver = Default + ".ForceDeliver";
        public const string MarkNotReceived = Default + ".MarkNotReceived";
    }

    public static class Customers
    {
        public const string Default = GroupName + ".Customers";
        public const string ContactView = Default + ".ContactView";
        public const string CashBanLift = Default + ".CashBanLift";
    }

    public static class Drivers
    {
        public const string Default = GroupName + ".Drivers";
        public const string DocumentsUpload = Default + ".DocumentsUpload";
        public const string DocumentsReview = Default + ".DocumentsReview";
        public const string ApproveRejectSuspend = Default + ".ApproveRejectSuspend";
        public const string PayoutWeekly = Default + ".PayoutWeekly";
    }

    public static class Dispatch
    {
        public const string Default = GroupName + ".Dispatch";
        public const string OfferRespond = Default + ".OfferRespond";
        public const string ManualAssign = Default + ".ManualAssign";
    }

    public static class Delivery
    {
        public const string Default = GroupName + ".Delivery";
        public const string Actions = Default + ".Actions";
        public const string CodeView = Default + ".CodeView";
        public const string TrackDriver = Default + ".TrackDriver";
    }

    public static class Zones
    {
        public const string Default = GroupName + ".Zones";
        public const string Manage = Default + ".Manage";
    }

    public static class Complaints
    {
        public const string Default = GroupName + ".Complaints";
        public const string Create = Default + ".Create";
        public const string Handle = Default + ".Handle";
    }

    public static class Compensation
    {
        public const string Default = GroupName + ".Compensation";
        public const string Credit = Default + ".Credit";
        public const string Approve = Default + ".Approve";
    }

    public static class Refunds
    {
        public const string Default = GroupName + ".Refunds";
        public const string ExecuteReal = Default + ".ExecuteReal";
        public const string RetryOrResolve = Default + ".RetryOrResolve";
    }

    public static class Cash
    {
        public const string Default = GroupName + ".Cash";
        public const string HandoverRecord = Default + ".HandoverRecord";
        public const string FloatIssue = Default + ".FloatIssue";
    }

    public static class Settlements
    {
        public const string Default = GroupName + ".Settlements";
        public const string View = Default + ".View";
        public const string GenerateConfirm = Default + ".GenerateConfirm";
        public const string MarkPaid = Default + ".MarkPaid";
    }

    public static class Ledger
    {
        public const string Default = GroupName + ".Ledger";
        public const string AdjustmentCreate = Default + ".AdjustmentCreate";
        public const string AdjustmentApprove = Default + ".AdjustmentApprove";
        public const string Writeoff = Default + ".Writeoff";
        public const string BalancesView = Default + ".BalancesView";
    }

    public static class Pricing
    {
        public const string Default = GroupName + ".Pricing";
        public const string Manage = Default + ".Manage";
    }

    public static class Coupons
    {
        public const string Default = GroupName + ".Coupons";
        public const string Manage = Default + ".Manage";
    }

    public static class Users
    {
        public const string Default = GroupName + ".Users";
        public const string RoleManage = Default + ".RoleManage";
    }

    public static class SystemSettings
    {
        public const string Default = GroupName + ".SystemSettings";
        public const string Manage = Default + ".Manage";
    }

    public static class Reports
    {
        public const string Default = GroupName + ".Reports";
        public const string Operational = Default + ".Operational";
        public const string Financial = Default + ".Financial";
    }

    public static class Audit
    {
        public const string Default = GroupName + ".Audit";
        public const string View = Default + ".View";
    }

    public static class SmsLogs
    {
        public const string Default = GroupName + ".SmsLogs";
        public const string View = Default + ".View";
    }
}
