using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
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
 * - customers and drivers receive messages addressed to their own user id;
 * - in-app calls (TD-03): the two apps exchange WebRTC signalling through SendCallSignal, which
 *   only forwards to the other side of a live call — the server never hands out a phone number.
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
    private readonly UserPresenceTracker _userPresence;
    private readonly VoiceCallManager _voiceCallManager;

    public OrdersHub(
        ICurrentScope currentScope,
        IPermissionChecker permissionChecker,
        TabletPresenceTracker tabletPresence,
        IUnitOfWorkManager unitOfWorkManager,
        UserPresenceTracker userPresence,
        VoiceCallManager voiceCallManager)
    {
        _userPresence = userPresence;
        _voiceCallManager = voiceCallManager;
        _currentScope = currentScope;
        _permissionChecker = permissionChecker;
        _tabletPresence = tabletPresence;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public static string RestaurantGroup(Guid restaurantId) => "restaurant:" + restaurantId;

    public override async Task OnConnectedAsync()
    {
        if (CurrentUser.Id.HasValue)
        {
            _userPresence.Connected(CurrentUser.Id.Value);
        }

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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (CurrentUser.Id.HasValue)
        {
            _userPresence.Disconnected(CurrentUser.Id.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /* WebRTC signalling of an in-app call (offer, answer, ice, ...): forwarded as "callSignal" to the
     * other participant only. Returns false when the sender is not in that live call. */
    public async Task<bool> SendCallSignal(Guid callId, string kind, object? payload)
    {
        if (!CurrentUser.Id.HasValue || string.IsNullOrWhiteSpace(kind) || kind.Length > 32)
        {
            return false;
        }

        Guid? peer;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            peer = await _voiceCallManager.GetPeerAsync(callId, CurrentUser.Id.Value);
            await uow.CompleteAsync();
        }

        if (peer == null)
        {
            return false;
        }

        await Clients.User(peer.Value.ToString()).SendAsync(CallNotifier.SignalMethod, new { callId, kind, payload });
        return true;
    }

    /* DER-OPS-01: a connecting tablet counts as seen; afterwards it pings RestaurantProfile/Heartbeat. */
    private async Task MarkTabletSeenAsync(Guid restaurantId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        await _tabletPresence.MarkSeenAsync(restaurantId);
        await uow.CompleteAsync();
    }
}
