using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Services.Dtos.Shared;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Orders;
using Saham.Services.Orders;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Customers;

/* The customer's orders. A customer only ever sees their own (order.view ◐). */
[Authorize(SahamPermissions.Orders.View)]
public class CustomerOrderAppService : SahamAppService, ICustomerOrderAppService
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly OrderManager _orderManager;
    private readonly OrderDtoMapper _mapper;
    private readonly DeliveryManager _deliveryManager;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<Volo.Abp.Identity.IdentityUser, Guid> _userRepository;

    public CustomerOrderAppService(
        IRepository<Order, Guid> orderRepository,
        OrderManager orderManager,
        OrderDtoMapper mapper,
        DeliveryManager deliveryManager,
        IRepository<Driver, Guid> driverRepository,
        IRepository<Volo.Abp.Identity.IdentityUser, Guid> userRepository)
    {
        _orderRepository = orderRepository;
        _orderManager = orderManager;
        _mapper = mapper;
        _deliveryManager = deliveryManager;
        _driverRepository = driverRepository;
        _userRepository = userRepository;
    }

    /* Confirm the order from a quote. A double tap or a retry with the same
     * Idempotency-Key returns the first order instead of creating a second (DER-CUS-06). */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.Create)]
    public async Task<CustomerOrderDto> CreateAsync(CreateOrderDto input)
    {
        var order = await _orderManager.CreateFromQuoteAsync(
            CurrentUser.GetId(),
            input.QuoteId,
            input.PaymentMethod,
            input.CashTendered,
            input.RecipientName,
            input.RecipientPhone);

        await _orderRepository.InsertAsync(order, autoSave: true); // autoSave: the number comes from a DB sequence
        return await MapAsync(order);
    }

    public async Task<CustomerOrderDto> GetAsync(Guid id)
    {
        return await MapAsync(await GetOwnAsync(id));
    }

    public async Task<PagedResultDto<CustomerOrderDto>> GetListAsync(GetCustomerOrdersInput input)
    {
        var customerId = CurrentUser.GetId();
        var queryable = (await _orderRepository.WithDetailsAsync(o => o.Items)).Where(o => o.CustomerId == customerId);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var orders = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(o => o.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<CustomerOrderDto>(totalCount, await _mapper.ToCustomerAsync(orders));
    }

    /* Free cancellation before the restaurant accepts (BR-045); later only through support. */
    [Idempotent]
    [Authorize(SahamPermissions.Orders.CancelCustomer)]
    public async Task<CustomerOrderDto> CancelAsync(Guid id)
    {
        var order = await GetOwnAsync(id);
        order.CancelByCustomer(CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* Answer to "an item ran out": continue without it, or cancel the order (REQ-CUS-21, MVP). */
    [Authorize(SahamPermissions.Orders.ItemDecision)]
    public async Task<CustomerOrderDto> DecideItemsAsync(Guid id, ItemDecisionDto input)
    {
        var order = await GetOwnAsync(id);
        order.ResolveItemDecision(input.Decision, ActorType.Customer, CurrentUser.GetId(), Clock.Now, GuidGenerator.Create);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    [Authorize(SahamPermissions.Delivery.TrackDriver)]
    public async Task<OrderTrackingDto> GetTrackingAsync(Guid id)
    {
        var order = await GetOwnAsync(id);
        var dto = new OrderTrackingDto
        {
            OrderId = order.Id,
            Number = order.Number,
            Status = order.Status,
            HoldReason = order.HoldReason,
            PickedUpAt = order.PickedUpAt,
            DeliveredAt = order.DeliveredAt,
            EtaMinAt = order.EtaMinAt,
            EtaMaxAt = order.EtaMaxAt,
            DeliveryCode = await AuthorizationService.IsGrantedAsync(SahamPermissions.Delivery.CodeView)
                ? _deliveryManager.RevealCode(order)
                : null
        };

        var assignment = await _deliveryManager.FindActiveAssignmentAsync(order.Id);
        if (assignment != null)
        {
            var driver = await _driverRepository.GetAsync(assignment.DriverId);
            var name = (await _userRepository.FindAsync(driver.Id))?.Name;
            dto.DriverAssigned = true;
            dto.DriverFirstName = name.IsNullOrWhiteSpace() ? null : name!.Trim().Split(' ')[0];
            dto.DriverLocation = driver.LastLocation == null ? null : new LocationDto { Latitude = driver.LastLocation.Y, Longitude = driver.LastLocation.X };
            dto.DriverLocationAt = driver.LastLocationAt;
        }

        return dto;
    }

    /* DER-CUS-08: "I received my order" - proof of delivery from the customer's own app. */
    [Idempotent]
    public async Task<CustomerOrderDto> ConfirmReceivedAsync(Guid id)
    {
        var order = await GetOwnAsync(id);
        await _deliveryManager.ConfirmByCustomerAsync(order);

        await _orderRepository.UpdateAsync(order);
        return await MapAsync(order);
    }

    /* Another customer's order is "not found". */
    private async Task<Order> GetOwnAsync(Guid id)
    {
        var order = await _orderRepository.GetAsync(id);
        if (order.CustomerId != CurrentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(Order), id);
        }

        return order;
    }

    private async Task<CustomerOrderDto> MapAsync(Order order) => (await _mapper.ToCustomerAsync(new[] { order })).Single();
}
