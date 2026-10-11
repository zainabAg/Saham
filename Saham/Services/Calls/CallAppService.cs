using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Saham.Integrations.Calls;
using Saham.Services.Dtos.Calls;
using Saham.Settings;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace Saham.Services.Calls;

public interface ICallAppService : IApplicationService
{
    Task<ListResultDto<CallSessionDto>> GetActiveAsync();

    Task<CallSessionDto> AnswerAsync(Guid id);

    Task<CallSessionDto> DeclineAsync(Guid id);

    Task<CallSessionDto> EndAsync(Guid id, EndCallDto input);
}

/* The other side of an in-app call (TD-03): the customer sees the ringing call (also by reading
 * GetActive when the app comes back), answers or declines; either side hangs up. Only the two
 * participants of a call can touch it — anybody else gets "not found". The driver starts calls
 * from his task (DriverTask/CallCustomer). */
[Authorize]
public class CallAppService : SahamAppService, ICallAppService
{
    private readonly VoiceCallManager _callManager;
    private readonly IRepository<ContactAttempt, Guid> _attemptRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IceServerProvider _iceServers;

    public CallAppService(
        VoiceCallManager callManager,
        IRepository<ContactAttempt, Guid> attemptRepository,
        IRepository<Order, Guid> orderRepository,
        IceServerProvider iceServers)
    {
        _callManager = callManager;
        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _iceServers = iceServers;
    }

    public async Task<ListResultDto<CallSessionDto>> GetActiveAsync()
    {
        var calls = await _callManager.GetLiveForCustomerAsync(CurrentUser.GetId());
        var result = new List<CallSessionDto>();
        foreach (var call in calls)
        {
            result.Add(await MapAsync(call, withIce: true));
        }

        return new ListResultDto<CallSessionDto>(result);
    }

    public async Task<CallSessionDto> AnswerAsync(Guid id)
    {
        var call = await _callManager.AnswerAsync(id, CurrentUser.GetId());
        await _attemptRepository.UpdateAsync(call);
        return await MapAsync(call, withIce: true);
    }

    public async Task<CallSessionDto> DeclineAsync(Guid id)
    {
        var call = await _callManager.DeclineAsync(id, CurrentUser.GetId());
        await _attemptRepository.UpdateAsync(call);
        return await MapAsync(call, withIce: false);
    }

    public async Task<CallSessionDto> EndAsync(Guid id, EndCallDto input)
    {
        var call = await _callManager.EndAsync(id, CurrentUser.GetId(), input.Failed);
        await _attemptRepository.UpdateAsync(call);
        return await MapAsync(call, withIce: false);
    }

    private async Task<CallSessionDto> MapAsync(ContactAttempt call, bool withIce)
    {
        var order = await _orderRepository.GetAsync(call.OrderId, includeDetails: false);
        return CallMapper.Map(call, order.Number, await SettingProvider.GetAsync<int>(SahamSettings.Calls.RingTimeoutSeconds),
            withIce ? _iceServers.GetFor(CurrentUser.GetId()) : new List<IceServer>());
    }
}

internal static class CallMapper
{
    public static CallSessionDto Map(ContactAttempt call, long orderNumber, int ringTimeoutSeconds, IEnumerable<IceServer> iceServers) => new()
    {
        CallId = call.Id,
        OrderId = call.OrderId,
        OrderNumber = orderNumber,
        Status = call.Outcome,
        StartedAt = call.StartedAt,
        AnsweredAt = call.AnsweredAt,
        EndedAt = call.EndedAt,
        DurationSeconds = call.DurationSeconds,
        RingTimeoutSeconds = ringTimeoutSeconds,
        IceServers = iceServers.Select(s => new IceServerDto { Urls = s.Urls.ToList(), Username = s.Username, Credential = s.Credential }).ToList()
    };
}
