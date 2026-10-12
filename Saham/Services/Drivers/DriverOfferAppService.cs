using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Drivers;

/* The driver's offers and current tasks (REQ-DRV-04, UC-16). A driver only sees offers made to
 * him and orders assigned to him (dispatch.offer.respond ◐). */
[Authorize(SahamPermissions.Dispatch.OfferRespond)]
public class DriverOfferAppService : SahamAppService, IDriverOfferAppService
{
    private readonly IRepository<DeliveryOffer, Guid> _offerRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly DispatchManager _dispatchManager;
    private readonly DriverTaskMapper _mapper;
    private readonly ICurrentScope _currentScope;

    public DriverOfferAppService(
        IRepository<DeliveryOffer, Guid> offerRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Order, Guid> orderRepository,
        DispatchManager dispatchManager,
        DriverTaskMapper mapper,
        ICurrentScope currentScope)
    {
        _offerRepository = offerRepository;
        _assignmentRepository = assignmentRepository;
        _orderRepository = orderRepository;
        _dispatchManager = dispatchManager;
        _mapper = mapper;
        _currentScope = currentScope;
    }

    public async Task<DriverOfferDto?> GetCurrentAsync()
    {
        var driver = await _currentScope.GetDriverAsync();
        var now = Clock.Now;

        var offer = await _offerRepository.FirstOrDefaultAsync(o =>
            o.DriverId == driver.Id && o.Status == OfferStatus.Pending && o.ExpiresAt >= now);
        if (offer == null)
        {
            return null;
        }

        return await _mapper.ToOfferAsync(offer, await _orderRepository.GetAsync(offer.OrderId), now);
    }

    /* BR-029, atomic. A late accept is 409 OFFER_EXPIRED (EX-05); if the order was taken in the
     * same instant, the unique index answers 409 ORDER_ALREADY_ASSIGNED (EX-06). */
    [Idempotent]
    public async Task<DriverTaskDto> AcceptAsync(Guid id)
    {
        var driver = await _currentScope.GetDriverAsync();
        var assignment = await _dispatchManager.AcceptOfferAsync(id, driver.Id);

        try
        {
            await CurrentUnitOfWork!.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AppDeliveryAssignments_OneActivePerOrder" })
        {
            throw new BusinessException(SahamErrorCodes.OrderAlreadyAssigned);
        }

        return await _mapper.ToTaskAsync(assignment, await _orderRepository.GetAsync(assignment.OrderId));
    }

    public async Task DeclineAsync(Guid id)
    {
        var driver = await _currentScope.GetDriverAsync();
        await _dispatchManager.DeclineOfferAsync(id, driver.Id);
    }

    /* At most two (BR-030). */
    public async Task<ListResultDto<DriverTaskDto>> GetTasksAsync()
    {
        var driver = await _currentScope.GetDriverAsync();
        var assignments = await _assignmentRepository.GetListAsync(a =>
            a.DriverId == driver.Id && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));

        var tasks = new List<DriverTaskDto>();
        foreach (var assignment in assignments.OrderBy(a => a.AssignedAt))
        {
            tasks.Add(await _mapper.ToTaskAsync(assignment, await _orderRepository.GetAsync(assignment.OrderId)));
        }

        return new ListResultDto<DriverTaskDto>(tasks);
    }
}
