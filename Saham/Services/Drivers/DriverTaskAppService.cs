using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Drivers;

/* The driver's steps on a task (REQ-DRV-06..10): arrived at restaurant → pickup → arrived at
 * customer → deliver with the code, or report that the customer does not answer. A driver acts
 * only on his own active assignment; a replayed action on a task taken away from him is
 * 409 ASSIGNMENT_RELEASED (SRS 10.7 §4). All actions are idempotent (EX-11). */
[Authorize(SahamPermissions.Delivery.Actions)]
public class DriverTaskAppService : SahamAppService, IDriverTaskAppService
{
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly DeliveryManager _deliveryManager;
    private readonly DispatchManager _dispatchManager;
    private readonly DriverTaskMapper _mapper;
    private readonly ICurrentScope _currentScope;

    public DriverTaskAppService(
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Order, Guid> orderRepository,
        IRepository<Driver, Guid> driverRepository,
        DeliveryManager deliveryManager,
        DispatchManager dispatchManager,
        DriverTaskMapper mapper,
        ICurrentScope currentScope)
    {
        _assignmentRepository = assignmentRepository;
        _orderRepository = orderRepository;
        _driverRepository = driverRepository;
        _deliveryManager = deliveryManager;
        _dispatchManager = dispatchManager;
        _mapper = mapper;
        _currentScope = currentScope;
    }

    [Idempotent]
    public async Task<DriverTaskDto> ArriveAtRestaurantAsync(Guid assignmentId, DriverActionDto input)
    {
        var (assignment, order, driver) = await LoadAsync(assignmentId);
        await _deliveryManager.ArrivedAtRestaurantAsync(order, assignment, driver, input.ClientOccurredAt);
        return await SaveAndMapAsync(assignment, order);
    }

    [Idempotent]
    public async Task<DriverTaskDto> PickUpAsync(Guid assignmentId, DriverActionDto input)
    {
        var (assignment, order, driver) = await LoadAsync(assignmentId);
        await _deliveryManager.PickUpAsync(order, assignment, PickedUpByType.Driver, driver.Id, input.ClientOccurredAt);
        return await SaveAndMapAsync(assignment, order);
    }

    [Idempotent]
    public async Task<DriverTaskDto> ArriveAtCustomerAsync(Guid assignmentId, DriverActionDto input)
    {
        var (assignment, order, driver) = await LoadAsync(assignmentId);
        _deliveryManager.ArrivedAtCustomer(order, assignment, driver.Id, input.ClientOccurredAt);
        return await SaveAndMapAsync(assignment, order);
    }

    /* A wrong code is a normal answer (Delivered=false, attempts left), so it is counted and saved. */
    [Idempotent]
    public async Task<DeliverResultDto> DeliverAsync(Guid assignmentId, DeliverDto input)
    {
        var (assignment, order, driver) = await LoadAsync(assignmentId);
        var result = await _deliveryManager.DeliverWithCodeAsync(order, assignment, driver, input.Code, input.ClientOccurredAt);

        return new DeliverResultDto
        {
            Delivered = result.Delivered,
            Locked = result.Locked,
            AttemptsLeft = result.AttemptsLeft,
            Task = await SaveAndMapAsync(assignment, order)
        };
    }

    [Idempotent]
    public async Task<DriverTaskDto> ReportCustomerNotAnsweringAsync(Guid assignmentId)
    {
        var (assignment, order, driver) = await LoadAsync(assignmentId);
        await _deliveryManager.ReportCustomerNotAnsweringAsync(order, assignment, driver.Id);
        return await SaveAndMapAsync(assignment, order);
    }

    /* BR-033: the driver gives the task back before pickup; dispatch looks for someone else. */
    [Idempotent]
    public async Task GiveUpAsync(Guid assignmentId, ReasonDto input)
    {
        var (_, order, driver) = await LoadAsync(assignmentId);
        await _dispatchManager.ReleaseActiveAsync(order, "DRIVER_GAVE_UP: " + input.Reason, ActorType.Driver, driver.Id);
        await _orderRepository.UpdateAsync(order);
    }

    private async Task<(DeliveryAssignment, Order, Driver)> LoadAsync(Guid assignmentId)
    {
        var driver = await _currentScope.GetDriverAsync();
        var assignment = await _assignmentRepository.GetAsync(assignmentId);
        assignment.EnsureActiveFor(driver.Id);
        return (assignment, await _orderRepository.GetAsync(assignment.OrderId), driver);
    }

    private async Task<DriverTaskDto> SaveAndMapAsync(DeliveryAssignment assignment, Order order)
    {
        await _assignmentRepository.UpdateAsync(assignment);
        await _orderRepository.UpdateAsync(order);
        return await _mapper.ToTaskAsync(assignment, order);
    }
}
