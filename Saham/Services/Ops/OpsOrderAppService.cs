using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Saham.Services.Dtos.Finance;
using Saham.Entities.Support;
using Saham.Services.Dtos.Ops;
using Saham.Services.Dtos.Orders;
using Saham.Entities.Notifications;
using Saham.Entities.Restaurants;
using Saham.Settings;
using System.Collections.Generic;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using Saham.Services.Orders;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace Saham.Services.Ops;

/* Live orders board for operations, owner (and accountant, read only) — REQ-OPS-01.
 * Restaurant staff use RestaurantOrderAppService; customers have order.view for their own
 * orders only, so this service is closed to both. */
[Authorize(SahamPermissions.Orders.View)]
public class OpsOrderAppService : SahamAppService, IOpsOrderAppService
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly ICurrentScope _currentScope;
    private readonly OrderDtoMapper _mapper;
    private readonly DispatchManager _dispatchManager;
    private readonly DeliveryManager _deliveryManager;
    private readonly IRepository<ContactAttempt, Guid> _contactRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Incident, Guid> _incidentRepository;
    private readonly IRepository<Notification, Guid> _notificationRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;

    public OpsOrderAppService(
        IRepository<Order, Guid> orderRepository,
        ICurrentScope currentScope,
        OrderDtoMapper mapper,
        DispatchManager dispatchManager,
        DeliveryManager deliveryManager,
        IRepository<ContactAttempt, Guid> contactRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Incident, Guid> incidentRepository,
        IRepository<Notification, Guid> notificationRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<IdentityUser, Guid> userRepository)
    {
        _assignmentRepository = assignmentRepository;
        _incidentRepository = incidentRepository;
        _notificationRepository = notificationRepository;
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
        _deliveryManager = deliveryManager;
        _contactRepository = contactRepository;
        _orderRepository = orderRepository;
        _currentScope = currentScope;
        _mapper = mapper;
        _dispatchManager = dispatchManager;
    }

    public async Task<PagedResultDto<OpsOrderDto>> GetListAsync(GetOpsOrdersInput input)
    {
        await CheckCompanyStaffAsync();

        var queryable = (await _orderRepository.WithDetailsAsync(o => o.Items))
            .WhereIf(input.ActiveOnly, o => o.Status != OrderStatus.Delivered
                                            && o.Status != OrderStatus.Cancelled
                                            && o.Status != OrderStatus.DeliveryFailed)
            .WhereIf(input.Status.HasValue, o => o.Status == input.Status)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .WhereIf(input.Number.HasValue, o => o.Number == input.Number);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var orders = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(o => o.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<OpsOrderDto>(totalCount, await _mapper.ToOpsAsync(orders, includeHistory: false));
    }

    /* REQ-OPS-01 / BR-058: every order in progress with its restaurant, driver, age and time left,
     * flagged when it is late or needs someone to act; those come first. The board refreshes on
     * "orderChanged" / "incidentChanged" from OrdersHub. */
    public async Task<ListResultDto<LiveOrderDto>> GetLiveAsync(GetLiveOrdersInput input)
    {
        await CheckCompanyStaffAsync();

        var now = Clock.Now;
        var lateThreshold = await SettingProvider.GetAsync<int>(SahamSettings.Orders.LateThresholdMinutes);
        var alertBefore = now.AddMinutes(-await SettingProvider.GetAsync<int>(SahamSettings.Orders.RestaurantAlertMinutes));

        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Placed || o.Status == OrderStatus.Accepted ||
                        o.Status == OrderStatus.Ready || o.Status == OrderStatus.PickedUp)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId));
        var ids = orders.Select(o => o.Id).ToList();

        var assignments = (await _assignmentRepository.GetListAsync(a => ids.Contains(a.OrderId) &&
                (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp)))
            .GroupBy(a => a.OrderId).ToDictionary(g => g.Key, g => g.First().DriverId);
        var incidents = (await _incidentRepository.GetListAsync(i => i.OrderId != null && ids.Contains(i.OrderId.Value) &&
                i.Status != IncidentStatus.Resolved))
            .ToLookup(i => i.OrderId!.Value, i => i.IncidentType);
        var failedNotifications = (await AsyncExecuter.ToListAsync((await _notificationRepository.GetQueryableAsync())
            .Where(n => n.OrderId != null && ids.Contains(n.OrderId.Value) &&
                        n.Status == NotificationStatus.Dead && n.Channel == NotificationChannel.Sms)
            .Select(n => n.OrderId!.Value))).ToHashSet();

        var restaurantIds = orders.Select(o => o.RestaurantId).Distinct().ToList();
        var restaurants = (await _restaurantRepository.GetListAsync(r => restaurantIds.Contains(r.Id), includeDetails: false))
            .ToDictionary(r => r.Id, r => r.Name.Ar);
        var driverIds = assignments.Values.Distinct().ToList();
        var drivers = (await _userRepository.GetListAsync(u => driverIds.Contains(u.Id)))
            .ToDictionary(u => u.Id, u => u.Name ?? u.UserName);

        var live = orders.Select(o =>
            {
                Guid? driverId = assignments.TryGetValue(o.Id, out var d) ? d : null;
                var dto = new LiveOrderDto
                {
                    Id = o.Id,
                    Number = o.Number,
                    Status = o.Status,
                    HoldReason = o.HoldReason,
                    DispatchStatus = o.DispatchStatus,
                    RestaurantId = o.RestaurantId,
                    RestaurantName = restaurants.GetValueOrDefault(o.RestaurantId, string.Empty),
                    DriverId = driverId,
                    DriverName = driverId.HasValue ? drivers.GetValueOrDefault(driverId.Value) : null,
                    PaymentMethod = o.PaymentMethod,
                    AmountDue = o.AmountDue,
                    PlacedAt = o.PlacedAt,
                    AgeMinutes = (int)Math.Floor((now - (o.PlacedAt ?? o.CreationTime)).TotalMinutes),
                    EtaMaxAt = o.EtaMaxAt,
                    RemainingMinutes = (int)Math.Floor((o.EtaMaxAt - now).TotalMinutes),
                    IsLate = now > o.EtaMaxAt.AddMinutes(lateThreshold),
                    OpenIncidents = incidents[o.Id].Distinct().ToList()
                };

                var reasons = dto.AttentionReasons;
                if (dto.IsLate) reasons.Add(AttentionReasons.Late);
                if (o.HoldReason.HasValue) reasons.Add(AttentionReasons.OnHold);
                if (o.Status == OrderStatus.Placed && o.PlacedAt <= alertBefore) reasons.Add(AttentionReasons.WaitingForRestaurant);
                if (dto.OpenIncidents.Contains(IncidentType.NoDriver)) reasons.Add(AttentionReasons.NoDriver);
                if (o.OtpLocked) reasons.Add(AttentionReasons.DeliveryCodeLocked);
                if (failedNotifications.Contains(o.Id)) reasons.Add(AttentionReasons.NotificationFailed);
                if (dto.OpenIncidents.Count > 0) reasons.Add(AttentionReasons.OpenIncident);
                dto.NeedsAttention = reasons.Count > 0;
                return dto;
            })
            .Where(o => !input.AttentionOnly || o.NeedsAttention)
            .OrderByDescending(o => o.NeedsAttention)
            .ThenByDescending(o => o.IsLate)
            .ThenBy(o => o.RemainingMinutes)
            .ToList();

        return new ListResultDto<LiveOrderDto>(live);
    }

    public async Task<OpsOrderDto> GetAsync(Guid id)
    {
        await CheckCompanyStaffAsync();
        return await MapAsync(await GetWithHistoryAsync(id));
    }

    /* order.cancel.ops ✱: any state before pickup, reason mandatory, and who caused it
     * (company / restaurant / customer after acceptance — BR-045, BR-069). */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.CancelOps)]
    public async Task<OpsOrderDto> CancelAsync(Guid id, OpsCancelOrderDto input)
    {
        var order = await GetWithHistoryAsync(id);
        order.CancelByOps(input.Note, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create, input.Cause);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* order.item_decision ✱: ops records the customer's answer after calling them (UC-12 A3). */
    [Authorize(SahamPermissions.Orders.ItemDecision)]
    public async Task<OpsOrderDto> DecideItemsAsync(Guid id, ItemDecisionDto input)
    {
        await CheckCompanyStaffAsync();

        var order = await GetWithHistoryAsync(id);
        order.ResolveItemDecision(input.Decision, ActorType.Staff, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* dispatch.manual_assign ✱: give the order to a chosen driver (also a second order on the way,
     * BR-031), replacing a driver who has not picked it up yet. */
    [Idempotent]
    [Authorize(SahamPermissions.Dispatch.ManualAssign)]
    public async Task<OpsOrderDto> AssignDriverAsync(Guid id, AssignDriverDto input)
    {
        var order = await GetWithHistoryAsync(id);
        await _dispatchManager.AssignManuallyAsync(order, input.DriverId, input.Reason, CurrentUser.GetId());
        await SaveOrConflictAsync();
        return await MapAsync(order);
    }

    /* Take the order away from its driver before pickup; dispatch starts again without him (BR-033). */
    [Authorize(SahamPermissions.Dispatch.ManualAssign)]
    public async Task<OpsOrderDto> ReleaseDriverAsync(Guid id, ReasonDto input)
    {
        var order = await GetWithHistoryAsync(id);
        await _dispatchManager.ReleaseActiveAsync(order, input.Reason, ActorType.Staff, CurrentUser.GetId());
        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* order.force_deliver (reason required): "delivered without code" after ops reached the customer
     * and checked (code failed, driver phone dead - EX-02). Who confirmed and why is stored on the order. */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.ForceDeliver)]
    public async Task<OpsOrderDto> ForceDeliverAsync(Guid id, ReasonDto input)
    {
        var order = await GetWithHistoryAsync(id);
        await _deliveryManager.ForceDeliverAsync(order, input.Reason, CurrentUser.GetId());

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* Ops record each call to the customer (no number is stored or shown to the driver, TD-03). */
    [Authorize(SahamPermissions.Orders.MarkNotReceived)]
    public async Task RecordContactAttemptAsync(Guid id, RecordContactAttemptDto input)
    {
        var order = await _orderRepository.GetAsync(id);
        var assignment = await _deliveryManager.FindActiveAssignmentAsync(order.Id);
        await _contactRepository.InsertAsync(new ContactAttempt(GuidGenerator.Create(), order.Id, assignment?.Id,
            CurrentUser.GetId(), input.Channel, input.Outcome, Clock.Now, input.Note));
    }

    /* order.mark_not_received (reason recorded): customer did not answer after the driver waited (BR-063).
     * Restaurant and driver are paid, the customer owes the cash amount (DEC-06). */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.MarkNotReceived)]
    public async Task<OpsOrderDto> MarkNotReceivedAsync(Guid id, MarkFailedDto input)
    {
        var order = await GetWithHistoryAsync(id);
        await _deliveryManager.MarkNotReceivedAsync(order, CurrentUser.GetId(), input.Note);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* EX-02 at +45 min: the driver never arrived; the customer is not charged, the company pays the restaurant. */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.MarkNotReceived)]
    public async Task<OpsOrderDto> MarkDriverIncidentAsync(Guid id, ReasonDto input)
    {
        var order = await GetWithHistoryAsync(id);
        await _deliveryManager.MarkDriverIncidentAsync(order, CurrentUser.GetId(), input.Reason);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* The unique "one active assignment per order" index is the last word in a race (EX-06). */
    private async Task SaveOrConflictAsync()
    {
        try
        {
            await CurrentUnitOfWork!.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new BusinessException(SahamErrorCodes.OrderAlreadyAssigned);
        }
    }

    /* Customers and drivers hold order.view for their own orders only, restaurant staff use
     * the restaurant service: this board is for company staff (ops, accountant, owner). */
    private async Task CheckCompanyStaffAsync()
    {
        if (await _currentScope.IsRestaurantStaffAsync() ||
            !(CurrentUser.IsInRole(SahamRoles.Ops) || CurrentUser.IsInRole(SahamRoles.Accountant) ||
              CurrentUser.IsInRole(SahamRoles.Owner)))
        {
            throw new AbpAuthorizationException();
        }
    }

    private async Task<Order> GetWithHistoryAsync(Guid id)
    {
        var queryable = await _orderRepository.WithDetailsAsync(o => o.Items, o => o.History);
        return await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(o => o.Id == id))
               ?? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Order), id);
    }

    private async Task<OpsOrderDto> MapAsync(Order order) => (await _mapper.ToOpsAsync(new[] { order }, includeHistory: true)).Single();
}
