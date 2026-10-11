using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Saham.Hubs;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Entities.Support;

/* SRS VoiceCallService (TD-03, BR-036, TC-73): the driver presses "call" and the two apps open a
 * voice call (WebRTC through the company's TURN server) — no phone number in any response, on
 * either side, ever:
 *   startCall      — only the assigned driver, only between assignment and delivery; the customer's
 *                    app must be connected, otherwise CUSTOMER_APP_OFFLINE (409) and the app offers
 *                    "ask ops to call" — the attempt is still recorded as evidence (BR-063);
 *   answer/decline — by the customer; nobody answering in time = NO_ANSWER (CallTimeoutWorker);
 *   end            — by either side, with the talk duration;
 *   requestOpsCall — opens a CONTACT_REQUEST incident: ops call the customer for the driver.
 * Every call is one contact_attempts row (channel IN_APP_VOICE). */
public class VoiceCallManager : DomainService
{
    private readonly IRepository<ContactAttempt, Guid> _attemptRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IncidentManager _incidentManager;
    private readonly UserPresenceTracker _presence;
    private readonly CallNotifier _notifier;
    private readonly ISettingProvider _settingProvider;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public VoiceCallManager(
        IRepository<ContactAttempt, Guid> attemptRepository,
        IRepository<Order, Guid> orderRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IncidentManager incidentManager,
        UserPresenceTracker presence,
        CallNotifier notifier,
        ISettingProvider settingProvider,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _incidentManager = incidentManager;
        _presence = presence;
        _notifier = notifier;
        _settingProvider = settingProvider;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<ContactAttempt> StartAsync(Order order, DeliveryAssignment assignment, Guid driverUserId)
    {
        EnsureActiveTask(order, assignment, driverUserId);

        var live = await _attemptRepository.FirstOrDefaultAsync(a => a.OrderId == order.Id && a.Channel == ContactChannel.InAppVoice && a.EndedAt == null);
        if (live != null)
        {
            return live; // a second tap re-joins the same call
        }

        if (!_presence.IsOnline(order.CustomerId))
        {
            // Kept even though the request fails: it is the evidence for "not received" (BR-063).
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                await _attemptRepository.InsertAsync(new ContactAttempt(GuidGenerator.Create(), order.Id, assignment.Id, driverUserId,
                    ContactChannel.InAppVoice, ContactOutcome.CustomerOffline, Clock.Now, null));
                await uow.CompleteAsync();
            }

            throw new BusinessException(SahamErrorCodes.CustomerAppOffline);
        }

        var call = await _attemptRepository.InsertAsync(new ContactAttempt(GuidGenerator.Create(), order.Id, assignment.Id, driverUserId,
            ContactChannel.InAppVoice, ContactOutcome.Ringing, Clock.Now, null), autoSave: true);

        // The customer sees the driver's first name and the order — never a number.
        var driverName = (await _userRepository.FindAsync(driverUserId))?.Name?.Trim().Split(' ')[0];
        _notifier.Queue(order.CustomerId, new
        {
            kind = "incoming",
            callId = call.Id,
            orderId = order.Id,
            orderNumber = order.Number,
            driverFirstName = driverName,
            ringTimeoutSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Calls.RingTimeoutSeconds)
        });
        return call;
    }

    public async Task<ContactAttempt> AnswerAsync(Guid callId, Guid customerUserId)
    {
        var (call, _, driverId) = await GetForCustomerAsync(callId, customerUserId);
        call.Answer(Clock.Now);
        _notifier.Queue(driverId, new { kind = "answered", callId });
        return call;
    }

    public async Task<ContactAttempt> DeclineAsync(Guid callId, Guid customerUserId)
    {
        var (call, _, driverId) = await GetForCustomerAsync(callId, customerUserId);
        call.Miss(Clock.Now);
        _notifier.Queue(driverId, new { kind = "declined", callId });
        return call;
    }

    public async Task<ContactAttempt> EndAsync(Guid callId, Guid userId, bool failed)
    {
        var (call, customerId, driverId) = await GetParticipantsAsync(callId);
        if (userId != customerId && userId != driverId)
        {
            throw new EntityNotFoundExceptionFor(callId);
        }

        call.End(Clock.Now, failed);
        _notifier.Queue(userId == driverId ? customerId : driverId, new { kind = "ended", callId, durationSeconds = call.DurationSeconds });
        return call;
    }

    /* For OrdersHub.SendCallSignal: the other side of a live call, or null if the sender is not in it. */
    public async Task<Guid?> GetPeerAsync(Guid callId, Guid userId)
    {
        var call = await _attemptRepository.FindAsync(callId);
        if (call is not { IsLive: true })
        {
            return null;
        }

        var order = await _orderRepository.GetAsync(call.OrderId, includeDetails: false);
        return userId == call.InitiatedBy ? order.CustomerId : userId == order.CustomerId ? call.InitiatedBy : null;
    }

    /* Calls still ringing after the timeout become "no answer" (both sides told). */
    public async Task<int> ExpireRingingAsync()
    {
        var timeout = await _settingProvider.GetAsync<int>(SahamSettings.Calls.RingTimeoutSeconds);
        var before = Clock.Now.AddSeconds(-timeout);
        var ringing = await _attemptRepository.GetListAsync(a =>
            a.Channel == ContactChannel.InAppVoice && a.EndedAt == null && a.Outcome == ContactOutcome.Ringing && a.StartedAt <= before);
        foreach (var call in ringing)
        {
            call.Miss(Clock.Now);
            await _attemptRepository.UpdateAsync(call);
            var order = await _orderRepository.GetAsync(call.OrderId, includeDetails: false);
            _notifier.Queue(call.InitiatedBy, new { kind = "missed", callId = call.Id });
            _notifier.Queue(order.CustomerId, new { kind = "missed", callId = call.Id });
        }

        return ringing.Count;
    }

    /* requestOpsCall: the customer's app is offline (or the call failed) — ops call on the driver's behalf. */
    public async Task<Incident?> RequestOpsCallAsync(Order order, DeliveryAssignment assignment, Guid driverUserId)
    {
        EnsureActiveTask(order, assignment, driverUserId);
        return await _incidentManager.OpenAsync(IncidentType.ContactRequest, IncidentSeverity.High, order.Id,
            orderId: order.Id, driverId: driverUserId, restaurantId: order.RestaurantId);
    }

    public async Task<List<ContactAttempt>> GetLiveForCustomerAsync(Guid customerUserId)
    {
        var orders = await _orderRepository.GetQueryableAsync();
        var attempts = await _attemptRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            from a in attempts
            join o in orders on a.OrderId equals o.Id
            where o.CustomerId == customerUserId && a.Channel == ContactChannel.InAppVoice && a.EndedAt == null
            select a);
    }

    /* BR-036: calls only while the driver holds the order — from assignment to delivery. */
    private static void EnsureActiveTask(Order order, DeliveryAssignment assignment, Guid driverUserId)
    {
        if (assignment.DriverId != driverUserId || assignment.OrderId != order.Id ||
            assignment.Status is not (AssignmentStatus.Assigned or AssignmentStatus.PickedUp))
        {
            throw new BusinessException(SahamErrorCodes.NoActiveAssignment);
        }
    }

    private async Task<(ContactAttempt Call, Guid CustomerId, Guid DriverId)> GetParticipantsAsync(Guid callId)
    {
        var call = await _attemptRepository.FindAsync(callId);
        if (call == null || call.Channel != ContactChannel.InAppVoice)
        {
            throw new EntityNotFoundExceptionFor(callId);
        }

        var order = await _orderRepository.GetAsync(call.OrderId, includeDetails: false);
        return (call, order.CustomerId, call.InitiatedBy);
    }

    private async Task<(ContactAttempt Call, Guid CustomerId, Guid DriverId)> GetForCustomerAsync(Guid callId, Guid customerUserId)
    {
        var participants = await GetParticipantsAsync(callId);
        if (participants.CustomerId != customerUserId)
        {
            throw new EntityNotFoundExceptionFor(callId);
        }

        return participants;
    }

    private sealed class EntityNotFoundExceptionFor : Volo.Abp.Domain.Entities.EntityNotFoundException
    {
        public EntityNotFoundExceptionFor(Guid id)
            : base(typeof(ContactAttempt), id)
        {
        }
    }
}
