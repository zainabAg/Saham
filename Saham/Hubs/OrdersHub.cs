using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Permissions;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* Realtime channel for order changes (REQ-RST-01, REQ-CUS-10), at /signalr-hubs/orders.
 * - restaurant staff join "restaurant:{id}" → the tablet hears new orders instantly;
 * - ops / owner join "ops" → live orders board and incident queue (REQ-OPS-01, DER-OPS-02);
 * - the accountant joins "finance" → financial incidents (cash overdue, cash cap);
 * - customers and drivers receive messages addressed to their own user id.
 * Clients only receive "something changed" signals and re-read details through the API,
 * so the hub never bypasses the per-role data scope. Mobile/web clients pass the access
 * token as ?access_token=... (WebSockets cannot send headers). */
[Authorize]
public class OrdersHub : AbpHub
{
    public const string OpsGroup = "ops";
    public const string FinanceGroup = "finance";

    private readonly ICurrentScope _currentScope;
    private readonly IPermissionChecker _permissionChecker;
    private readonly TabletPresenceTracker _tabletPresence;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public OrdersHub(
        ICurrentScope currentScope,
        IPermissionChecker permissionChecker,
        TabletPresenceTracker tabletPresence,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _currentScope = currentScope;
        _permissionChecker = permissionChecker;
        _tabletPresence = tabletPresence;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public static string RestaurantGroup(Guid restaurantId) => "restaurant:" + restaurantId;

    public override async Task OnConnectedAsync()
    {
        if (await _currentScope.IsRestaurantStaffAsync())
        {
            var restaurantId = await _currentScope.GetRestaurantIdAsync();
            await Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
            await MarkTabletSeenAsync(restaurantId);
        }
        else if (await _permissionChecker.IsGrantedAsync(SahamPermissions.Orders.CancelOps))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OpsGroup);
        }
        else if (await _permissionChecker.IsGrantedAsync(SahamPermissions.Cash.HandoverRecord))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, FinanceGroup);
        }

        await base.OnConnectedAsync();
    }

    /* DER-OPS-01: a connecting tablet counts as seen; afterwards it pings RestaurantProfile/Heartbeat. */
    private async Task MarkTabletSeenAsync(Guid restaurantId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        await _tabletPresence.MarkSeenAsync(restaurantId);
        await uow.CompleteAsync();
    }
}
