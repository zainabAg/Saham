using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Identity;
using Saham.Permissions;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Authorization.Permissions;

namespace Saham.Hubs;

/* Realtime channel for order changes (REQ-RST-01, REQ-CUS-10), at /signalr-hubs/orders.
 * - restaurant staff join "restaurant:{id}" → the tablet hears new orders instantly;
 * - ops / owner join "ops" → live orders board;
 * - customers receive messages addressed to their own user id.
 * Clients only receive "something changed" signals and re-read details through the API,
 * so the hub never bypasses the per-role data scope. Mobile/web clients pass the access
 * token as ?access_token=... (WebSockets cannot send headers). */
[Authorize]
public class OrdersHub : AbpHub
{
    public const string OpsGroup = "ops";

    private readonly ICurrentScope _currentScope;
    private readonly IPermissionChecker _permissionChecker;

    public OrdersHub(ICurrentScope currentScope, IPermissionChecker permissionChecker)
    {
        _currentScope = currentScope;
        _permissionChecker = permissionChecker;
    }

    public static string RestaurantGroup(System.Guid restaurantId) => "restaurant:" + restaurantId;

    public override async Task OnConnectedAsync()
    {
        if (await _currentScope.IsRestaurantStaffAsync())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(await _currentScope.GetRestaurantIdAsync()));
        }
        else if (await _permissionChecker.IsGrantedAsync(SahamPermissions.Orders.CancelOps))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OpsGroup);
        }

        await base.OnConnectedAsync();
    }
}
