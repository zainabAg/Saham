using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Orders;
using Saham.Services.Orders;
using Saham.Settings;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace Saham.Services.Restaurants;

/* The restaurant tablet (REQ-RST-01..05, REQ-RST-10): the restaurant's own orders only,
 * never pending-payment or not-yet-released scheduled ones (INV-08).
 * Accept / reject / ready / item unavailable / cancel: manager and kitchen. */
[Authorize(SahamPermissions.Orders.View)]
public class RestaurantOrderAppService : SahamAppService, IRestaurantOrderAppService
{
    private static readonly OrderStatus[] ActiveStatuses =
        { OrderStatus.Placed, OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.PickedUp };

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly ICurrentScope _currentScope;
    private readonly OrderDtoMapper _mapper;

    public RestaurantOrderAppService(IRepository<Order, Guid> orderRepository, ICurrentScope currentScope, OrderDtoMapper mapper)
    {
        _orderRepository = orderRepository;
        _currentScope = currentScope;
        _mapper = mapper;
    }

    public async Task<PagedResultDto<RestaurantOrderDto>> GetListAsync(GetRestaurantOrdersInput input)
    {
        var restaurantId = await _currentScope.GetRestaurantIdAsync();

        var queryable = (await _orderRepository.WithDetailsAsync(o => o.Items))
            .Where(o => o.RestaurantId == restaurantId
                        && o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Scheduled)
            .WhereIf(input.ActiveOnly, o => ActiveStatuses.Contains(o.Status))
            .WhereIf(input.Status.HasValue, o => o.Status == input.Status);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var orders = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(o => o.Status == OrderStatus.Placed ? 0 : 1) // new orders first
            .ThenByDescending(o => o.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<RestaurantOrderDto>(totalCount, await _mapper.ToRestaurantAsync(orders));
    }

    public async Task<RestaurantOrderDto> GetAsync(Guid id)
    {
        return await MapAsync(await GetInScopeAsync(id));
    }

    [Authorize(SahamPermissions.Orders.AcceptReject)]
    public async Task<RestaurantOrderDto> AcceptAsync(Guid id, AcceptOrderDto input)
    {
        var order = await GetInScopeAsync(id);
        order.Accept(
            input.PrepMinutes,
            await SettingProvider.GetAsync<int>(SahamSettings.Orders.MinPrepMinutes),
            await SettingProvider.GetAsync<int>(SahamSettings.Orders.MaxPrepMinutes),
            CurrentUser.GetId(),
            Clock.Now,
            GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    [Authorize(SahamPermissions.Orders.AcceptReject)]
    public async Task<RestaurantOrderDto> RejectAsync(Guid id, RejectOrderDto input)
    {
        var order = await GetInScopeAsync(id);
        order.Reject(input.Reason, input.Note, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    [Authorize(SahamPermissions.Orders.MarkReady)]
    public async Task<RestaurantOrderDto> MarkReadyAsync(Guid id)
    {
        var order = await GetInScopeAsync(id);
        order.MarkReady(CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* BR-047: the order is held and the customer decides within N minutes. The restaurant
     * never calls the customer; it gets the menu items it can switch off (DER-RST-06). */
    [Authorize(SahamPermissions.Orders.ItemUnavailable)]
    public async Task<ReportItemsUnavailableResultDto> ReportItemsUnavailableAsync(Guid id, ReportItemsUnavailableDto input)
    {
        var order = await GetInScopeAsync(id);
        var now = Clock.Now;
        var decisionMinutes = await SettingProvider.GetAsync<int>(SahamSettings.Orders.ItemDecisionMinutes);

        order.ReportItemsUnavailable(input.OrderItemIds, CurrentUser.GetId(), now.AddMinutes(decisionMinutes), now, GuidGenerator.Create);
        await _orderRepository.UpdateAsync(order);

        return new ReportItemsUnavailableResultDto
        {
            Order = await MapAsync(order),
            MenuItemIdsToSwitchOff = order.Items.Where(i => i.IsReportedUnavailable).Select(i => i.MenuItemId).Distinct().ToList()
        };
    }

    /* BR-046: cancelling an accepted order needs a reason and counts against the restaurant. */
    [Authorize(SahamPermissions.Orders.CancelRestaurant)]
    public async Task<RestaurantOrderDto> CancelAsync(Guid id, CancelOrderDto input)
    {
        var order = await GetInScopeAsync(id);
        order.CancelByRestaurant(input.Note, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* Orders of another restaurant, and orders the restaurant may not see yet, are "not found". */
    private async Task<Order> GetInScopeAsync(Guid id)
    {
        var restaurantId = await _currentScope.GetRestaurantIdAsync();
        var order = await _orderRepository.GetAsync(id);
        if (order.RestaurantId != restaurantId || !OrderStateMachine.IsVisibleToRestaurant(order.Status))
        {
            throw new EntityNotFoundException(typeof(Order), id);
        }

        return order;
    }

    private async Task<RestaurantOrderDto> MapAsync(Order order) => (await _mapper.ToRestaurantAsync(new[] { order })).Single();
}
