using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Orders;
using Saham.Services.Orders;
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

    public OpsOrderAppService(IRepository<Order, Guid> orderRepository, ICurrentScope currentScope, OrderDtoMapper mapper)
    {
        _orderRepository = orderRepository;
        _currentScope = currentScope;
        _mapper = mapper;
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
