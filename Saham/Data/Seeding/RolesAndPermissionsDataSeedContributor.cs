using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saham.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;
using P = Saham.Permissions.SahamPermissions;

namespace Saham.Data.Seeding;

/* Creates the eight roles of SRS 5.2 and grants them the permissions of the
 * RBAC matrix (SRS 5.3). ◐ (own scope only) is granted here and enforced in
 * code by ICurrentScope. The built-in "admin" user also gets OWNER.
 * Seeding only adds missing grants; the owner can adjust them later from the UI. */
public class RolesAndPermissionsDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private const string AdminUserName = "admin";

    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        [SahamRoles.Customer] = new[]
        {
            P.Restaurants.Browse,
            P.Orders.Create, P.Orders.View, P.Orders.CancelCustomer, P.Orders.ItemDecision,
            P.Delivery.CodeView, P.Delivery.TrackDriver,
            P.Complaints.Create,
            P.Ledger.BalancesView
        },
        [SahamRoles.RestaurantManager] = new[]
        {
            P.Restaurants.Browse, P.Restaurants.HoursMinOrder, P.Restaurants.Pause,
            P.Restaurants.UsersManage, P.Restaurants.CouponRequest,
            P.Menu.Manage, P.Menu.ToggleAvailability,
            P.Orders.View, P.Orders.AcceptReject, P.Orders.MarkReady, P.Orders.CancelRestaurant,
            P.Orders.ItemUnavailable, P.Orders.HandoverToDriver,
            P.Settlements.View, P.Ledger.BalancesView,
            P.Reports.Operational, P.Reports.Financial
        },
        [SahamRoles.RestaurantKitchen] = new[]
        {
            P.Restaurants.Browse, P.Restaurants.Pause,
            P.Menu.ToggleAvailability,
            P.Orders.View, P.Orders.AcceptReject, P.Orders.MarkReady, P.Orders.CancelRestaurant,
            P.Orders.ItemUnavailable
        },
        [SahamRoles.RestaurantCashier] = new[]
        {
            P.Restaurants.Browse,
            P.Orders.View, P.Orders.HandoverToDriver,
            P.Settlements.View, P.Ledger.BalancesView,
            P.Reports.Financial
        },
        [SahamRoles.Driver] = new[]
        {
            P.Orders.View,
            P.Drivers.DocumentsUpload,
            P.Dispatch.OfferRespond,
            P.Delivery.Actions,
            P.Settlements.View, P.Ledger.BalancesView,
            P.Reports.Operational
        },
        [SahamRoles.Ops] = new[]
        {
            P.Restaurants.Browse, P.Restaurants.Pause,
            P.Menu.ToggleAvailability,
            P.Orders.View, P.Orders.ItemDecision, P.Orders.CancelOps, P.Orders.Modify,
            P.Orders.ForceDeliver, P.Orders.MarkNotReceived,
            P.Customers.ContactView,
            P.Drivers.DocumentsReview,
            P.Dispatch.ManualAssign,
            P.Delivery.TrackDriver,
            P.Zones.Manage,
            P.Complaints.Handle,
            P.Compensation.Credit,
            P.Reports.Operational,
            P.Incidents.Handle,
            P.SmsLogs.View,
            P.OpsBoard.View,
            P.Notifications.View, P.Notifications.Retry
        },
        [SahamRoles.Accountant] = new[]
        {
            P.Restaurants.Browse,
            P.Orders.View,
            P.Refunds.ExecuteReal, P.Refunds.RetryOrResolve,
            P.Cash.HandoverRecord, P.Cash.FloatIssue,
            P.Drivers.PayoutWeekly,
            P.Settlements.View, P.Settlements.GenerateConfirm, P.Settlements.MarkPaid,
            P.Ledger.AdjustmentCreate, P.Ledger.BalancesView,
            P.Reports.Operational, P.Reports.Financial,
            P.Incidents.Handle /* financial incidents only (IncidentAppService) */
        },
        [SahamRoles.Owner] = new[]
        {
            P.Restaurants.Browse, P.Restaurants.Onboard, P.Restaurants.HoursMinOrder,
            P.Restaurants.Pause, P.Restaurants.UsersManage,
            P.Menu.Manage, P.Menu.ToggleAvailability,
            P.Orders.View, P.Orders.CancelOps, P.Orders.Modify, P.Orders.ForceDeliver, P.Orders.MarkNotReceived,
            P.Customers.ContactView, P.Customers.CashBanLift,
            P.Drivers.DocumentsReview, P.Drivers.ApproveRejectSuspend, P.Drivers.PayoutWeekly,
            P.Dispatch.ManualAssign,
            P.Delivery.TrackDriver,
            P.Zones.Manage,
            P.Complaints.Handle,
            P.Compensation.Credit, P.Compensation.Approve,
            P.Refunds.RetryOrResolve /* sees and retries refunds; returning money by hand stays with the accountant (BR-070) */,
            P.Cash.HandoverRecord, P.Cash.FloatIssue,
            P.Settlements.View, P.Settlements.GenerateConfirm, P.Settlements.MarkPaid,
            P.Ledger.AdjustmentCreate, P.Ledger.AdjustmentApprove, P.Ledger.Writeoff, P.Ledger.BalancesView,
            P.Pricing.Manage,
            P.Coupons.Manage,
            P.Users.RoleManage,
            P.SystemSettings.Manage,
            P.Reports.Operational, P.Reports.Financial,
            P.Audit.View,
            P.Incidents.Handle,
            P.SmsLogs.View,
            P.OpsBoard.View,
            P.Notifications.View, P.Notifications.Retry
        }
    };

    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IGuidGenerator _guidGenerator;

    public RolesAndPermissionsDataSeedContributor(
        IdentityRoleManager roleManager,
        IdentityUserManager userManager,
        IPermissionDataSeeder permissionDataSeeder,
        IGuidGenerator guidGenerator)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _permissionDataSeeder = permissionDataSeeder;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        foreach (var (roleName, permissions) in RolePermissions)
        {
            if (await _roleManager.FindByNameAsync(roleName) == null)
            {
                var role = new IdentityRole(_guidGenerator.Create(), roleName, context.TenantId)
                {
                    IsStatic = true,
                    IsPublic = false
                };
                (await _roleManager.CreateAsync(role)).CheckErrors();
            }

            await _permissionDataSeeder.SeedAsync(
                RolePermissionValueProvider.ProviderName,
                roleName,
                WithParents(permissions),
                context.TenantId);
        }

        var admin = await _userManager.FindByNameAsync(AdminUserName);
        if (admin != null && !await _userManager.IsInRoleAsync(admin, SahamRoles.Owner))
        {
            (await _userManager.AddToRoleAsync(admin, SahamRoles.Owner)).CheckErrors();
        }
    }

    /* ABP only grants a child permission when its parent is granted too. */
    private static IEnumerable<string> WithParents(IEnumerable<string> permissions)
    {
        return permissions
            .SelectMany(p => new[] { p[..p.LastIndexOf('.')], p })
            .Distinct();
    }
}
