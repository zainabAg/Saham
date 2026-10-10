using Saham.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Saham.Permissions;

public class SahamPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(SahamPermissions.GroupName);


        var booksPermission = myGroup.AddPermission(SahamPermissions.Books.Default, L("Permission:Books"));
        booksPermission.AddChild(SahamPermissions.Books.Create, L("Permission:Books.Create"));
        booksPermission.AddChild(SahamPermissions.Books.Edit, L("Permission:Books.Edit"));
        booksPermission.AddChild(SahamPermissions.Books.Delete, L("Permission:Books.Delete"));

        AddGroup(myGroup, SahamPermissions.Restaurants.Default,
            SahamPermissions.Restaurants.Browse,
            SahamPermissions.Restaurants.Onboard,
            SahamPermissions.Restaurants.HoursMinOrder,
            SahamPermissions.Restaurants.Pause,
            SahamPermissions.Restaurants.UsersManage,
            SahamPermissions.Restaurants.CouponRequest);

        AddGroup(myGroup, SahamPermissions.Menu.Default,
            SahamPermissions.Menu.Manage,
            SahamPermissions.Menu.ToggleAvailability);

        AddGroup(myGroup, SahamPermissions.Orders.Default,
            SahamPermissions.Orders.Create,
            SahamPermissions.Orders.View,
            SahamPermissions.Orders.CancelCustomer,
            SahamPermissions.Orders.ItemDecision,
            SahamPermissions.Orders.AcceptReject,
            SahamPermissions.Orders.MarkReady,
            SahamPermissions.Orders.CancelRestaurant,
            SahamPermissions.Orders.ItemUnavailable,
            SahamPermissions.Orders.HandoverToDriver,
            SahamPermissions.Orders.CancelOps,
            SahamPermissions.Orders.Modify,
            SahamPermissions.Orders.ForceDeliver,
            SahamPermissions.Orders.MarkNotReceived);

        AddGroup(myGroup, SahamPermissions.Customers.Default,
            SahamPermissions.Customers.ContactView,
            SahamPermissions.Customers.CashBanLift);

        AddGroup(myGroup, SahamPermissions.Drivers.Default,
            SahamPermissions.Drivers.DocumentsUpload,
            SahamPermissions.Drivers.DocumentsReview,
            SahamPermissions.Drivers.ApproveRejectSuspend,
            SahamPermissions.Drivers.PayoutWeekly);

        AddGroup(myGroup, SahamPermissions.Dispatch.Default,
            SahamPermissions.Dispatch.OfferRespond,
            SahamPermissions.Dispatch.ManualAssign);

        AddGroup(myGroup, SahamPermissions.Delivery.Default,
            SahamPermissions.Delivery.Actions,
            SahamPermissions.Delivery.CodeView,
            SahamPermissions.Delivery.TrackDriver);

        AddGroup(myGroup, SahamPermissions.Zones.Default,
            SahamPermissions.Zones.Manage);

        AddGroup(myGroup, SahamPermissions.Complaints.Default,
            SahamPermissions.Complaints.Create,
            SahamPermissions.Complaints.Handle);

        AddGroup(myGroup, SahamPermissions.Compensation.Default,
            SahamPermissions.Compensation.Credit,
            SahamPermissions.Compensation.Approve);

        AddGroup(myGroup, SahamPermissions.Refunds.Default,
            SahamPermissions.Refunds.ExecuteReal,
            SahamPermissions.Refunds.RetryOrResolve);

        AddGroup(myGroup, SahamPermissions.Cash.Default,
            SahamPermissions.Cash.HandoverRecord,
            SahamPermissions.Cash.FloatIssue);

        AddGroup(myGroup, SahamPermissions.Settlements.Default,
            SahamPermissions.Settlements.View,
            SahamPermissions.Settlements.GenerateConfirm,
            SahamPermissions.Settlements.MarkPaid);

        AddGroup(myGroup, SahamPermissions.Ledger.Default,
            SahamPermissions.Ledger.AdjustmentCreate,
            SahamPermissions.Ledger.AdjustmentApprove,
            SahamPermissions.Ledger.Writeoff,
            SahamPermissions.Ledger.BalancesView);

        AddGroup(myGroup, SahamPermissions.Pricing.Default,
            SahamPermissions.Pricing.Manage);

        AddGroup(myGroup, SahamPermissions.Coupons.Default,
            SahamPermissions.Coupons.Manage);

        AddGroup(myGroup, SahamPermissions.Users.Default,
            SahamPermissions.Users.RoleManage);

        AddGroup(myGroup, SahamPermissions.SystemSettings.Default,
            SahamPermissions.SystemSettings.Manage);

        AddGroup(myGroup, SahamPermissions.Reports.Default,
            SahamPermissions.Reports.Operational,
            SahamPermissions.Reports.Financial);

        AddGroup(myGroup, SahamPermissions.Audit.Default,
            SahamPermissions.Audit.View);

        AddGroup(myGroup, SahamPermissions.SmsLogs.Default,
            SahamPermissions.SmsLogs.View);
    }

    /* Adds a parent permission with its children; display names are "Permission:<name>". */
    private static void AddGroup(PermissionGroupDefinition group, string parent, params string[] children)
    {
        var parentPermission = group.AddPermission(parent, L("Permission:" + parent));
        foreach (var child in children)
        {
            parentPermission.AddChild(child, L("Permission:" + child));
        }
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SahamResource>(name);
    }
}
