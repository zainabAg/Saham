using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Ops;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Ops;

/* The incident queue (DER-OPS-02): ops and the owner see everything, the accountant sees the
 * financial incidents (cash overdue, cash cap). Highest severity and oldest first.
 * Each incident shows its status, who handles it, the action taken, a note and when it was closed.
 * Every change is pushed to the board over OrdersHub ("incidentChanged"). */
[Authorize(SahamPermissions.Incidents.Handle)]
public class IncidentAppService : SahamAppService, IIncidentAppService
{
    private readonly IRepository<Incident, Guid> _incidentRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IncidentManager _incidentManager;

    public IncidentAppService(
        IRepository<Incident, Guid> incidentRepository,
        IRepository<Order, Guid> orderRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IncidentManager incidentManager)
    {
        _incidentRepository = incidentRepository;
        _orderRepository = orderRepository;
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
        _incidentManager = incidentManager;
    }

    public async Task<PagedResultDto<IncidentDto>> GetListAsync(GetIncidentsInput input)
    {
        var financialOnly = IsFinanceOnly();
        var financial = Incident.FinancialTypes;

        var queryable = (await _incidentRepository.GetQueryableAsync())
            .WhereIf(financialOnly, i => financial.Contains(i.IncidentType))
            .WhereIf(input.OpenOnly && !input.Status.HasValue, i => i.Status != IncidentStatus.Resolved)
            .WhereIf(input.Status.HasValue, i => i.Status == input.Status)
            .WhereIf(input.IncidentType.HasValue, i => i.IncidentType == input.IncidentType)
            .WhereIf(input.AssignedTo.HasValue, i => i.AssignedTo == input.AssignedTo)
            .WhereIf(input.UnassignedOnly, i => i.AssignedTo == null)
            .WhereIf(input.OrderId.HasValue, i => i.OrderId == input.OrderId)
            .WhereIf(input.DriverId.HasValue, i => i.DriverId == input.DriverId)
            .WhereIf(input.RestaurantId.HasValue, i => i.RestaurantId == input.RestaurantId);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        // Severity is stored as text (D3): rank it explicitly, never alphabetically.
        var incidents = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(i => i.Status == IncidentStatus.Resolved ? 1 : 0)
            .ThenByDescending(i => i.Severity == IncidentSeverity.Critical ? 3
                : i.Severity == IncidentSeverity.High ? 2
                : i.Severity == IncidentSeverity.Medium ? 1 : 0)
            .ThenBy(i => i.OpenedAt)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<IncidentDto>(totalCount, await MapAsync(incidents));
    }

    public async Task<IncidentDto> GetAsync(Guid id)
    {
        return (await MapAsync(new List<Incident> { await GetInScopeAsync(id) })).Single();
    }

    /* Ops open an incident by hand, e.g. after a customer's call; it needs at least an order,
     * a driver or a restaurant. An open incident of the same type on the same subject is returned instead. */
    public async Task<IncidentDto> OpenAsync(OpenIncidentDto input)
    {
        if (IsFinanceOnly() && !Incident.FinancialTypes.Contains(input.IncidentType))
        {
            throw new AbpAuthorizationException();
        }

        if (!input.OrderId.HasValue && !input.DriverId.HasValue && !input.RestaurantId.HasValue)
        {
            throw new BusinessException(SahamErrorCodes.IncidentSubjectRequired);
        }

        var restaurantId = input.RestaurantId;
        if (input.OrderId.HasValue)
        {
            restaurantId ??= (await _orderRepository.GetAsync(input.OrderId.Value, includeDetails: false)).RestaurantId;
        }

        var incident = await _incidentManager.OpenManualAsync(input.IncidentType, input.Severity,
            input.OrderId, input.DriverId, restaurantId, input.Note);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetAsync(incident.Id);
    }

    public async Task<IncidentDto> AcknowledgeAsync(Guid id)
    {
        var incident = await GetInScopeAsync(id);
        incident.Acknowledge(CurrentUser.GetId());

        await _incidentRepository.UpdateAsync(incident);
        return await GetAsync(id);
    }

    /* Give the incident to a colleague who also handles incidents, or take it. */
    public async Task<IncidentDto> AssignAsync(Guid id, AssignIncidentDto input)
    {
        var incident = await GetInScopeAsync(id);
        var userId = input.UserId ?? CurrentUser.GetId();
        if (userId != CurrentUser.GetId() && !await _userRepository.AnyAsync(u => u.Id == userId))
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(IdentityUser), userId);
        }

        incident.AssignTo(userId);
        await _incidentRepository.UpdateAsync(incident);
        return await GetAsync(id);
    }

    public async Task<IncidentDto> UpdateNoteAsync(Guid id, IncidentNoteDto input)
    {
        var incident = await GetInScopeAsync(id);
        incident.UpdateNote(input.Note);

        await _incidentRepository.UpdateAsync(incident);
        return await GetAsync(id);
    }

    public async Task<IncidentDto> ResolveAsync(Guid id, ResolveIncidentDto input)
    {
        var incident = await GetInScopeAsync(id);
        incident.Resolve(input.Resolution, CurrentUser.GetId(), Clock.Now);

        await _incidentRepository.UpdateAsync(incident);
        return await GetAsync(id);
    }

    /* The accountant handles incidents too, but only the financial ones (DER-OPS-02). */
    private bool IsFinanceOnly() =>
        CurrentUser.IsInRole(SahamRoles.Accountant) &&
        !CurrentUser.IsInRole(SahamRoles.Ops) && !CurrentUser.IsInRole(SahamRoles.Owner);

    private async Task<Incident> GetInScopeAsync(Guid id)
    {
        var incident = await _incidentRepository.GetAsync(id);
        if (IsFinanceOnly() && !incident.IsFinancial)
        {
            throw new AbpAuthorizationException();
        }

        return incident;
    }

    private async Task<List<IncidentDto>> MapAsync(List<Incident> incidents)
    {
        var orderIds = incidents.Where(i => i.OrderId.HasValue).Select(i => i.OrderId!.Value).Distinct().ToList();
        var restaurantIds = incidents.Where(i => i.RestaurantId.HasValue).Select(i => i.RestaurantId!.Value).Distinct().ToList();
        var userIds = incidents.SelectMany(i => new[] { i.DriverId, i.AssignedTo }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number }));
        var numbers = orders.ToDictionary(o => o.Id, o => o.Number);
        var restaurants = (await _restaurantRepository.GetListAsync(r => restaurantIds.Contains(r.Id))).ToDictionary(r => r.Id, r => r.Name.Ar);
        var users = (await _userRepository.GetListAsync(u => userIds.Contains(u.Id))).ToDictionary(u => u.Id, u => u.Name ?? u.UserName);

        return incidents.Select(i =>
        {
            var dto = ObjectMapper.Map<Incident, IncidentDto>(i);
            dto.OrderNumber = i.OrderId.HasValue && numbers.TryGetValue(i.OrderId.Value, out var n) ? n : null;
            dto.RestaurantName = i.RestaurantId.HasValue ? restaurants.GetValueOrDefault(i.RestaurantId.Value) : null;
            dto.DriverName = i.DriverId.HasValue ? users.GetValueOrDefault(i.DriverId.Value) : null;
            dto.AssignedToName = i.AssignedTo.HasValue ? users.GetValueOrDefault(i.AssignedTo.Value) : null;
            return dto;
        }).ToList();
    }
}
