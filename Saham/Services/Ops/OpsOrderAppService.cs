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
using Saham.Services.Dtos.Orders;
using Saham.Services.Orders;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
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

    public OpsOrderAppService(
        IRepository<Order, Guid> orderRepository,
        ICurrentScope currentScope,
        OrderDtoMapper mapper,
        DispatchManager dispatchManager,
        DeliveryManager deliveryManager,
        IRepository<ContactAttempt, Guid> contactRepository)
    {
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

    public async Task<OpsOrderDto> GetAsync(Guid id)
    {
        await CheckCompanyStaffAsync();
        return await MapAsync(await GetWithHistoryAsync(id));
    }

    /* order.cancel.ops ✱: any state before pickup, reason mandatory. */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.CancelOps)]
    public async Task<OpsOrderDto> CancelAsync(Guid id, CancelOrderDto input)
    {
        var order = await GetWithHistoryAsync(id);
        order.CancelByOps(input.Note, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

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
