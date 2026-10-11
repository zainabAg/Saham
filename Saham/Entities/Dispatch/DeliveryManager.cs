using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Saham.Entities.Customers;
using Saham.Entities.Drivers;
using Saham.Entities.Notifications;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Entities.Shared;
using Saham.Entities.Support;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;

namespace Saham.Entities.Dispatch;

/* From the restaurant counter to the customer's door (WF-04, UC "pickup and delivery"):
 *   arrived at restaurant (≤ 200 m) → pickup by driver or cashier (+ delivery code by SMS)
 *   → arrived at customer → delivered with a proof (BR-034): the code typed by the driver,
 *   the customer's own "I received it", or an ops override with a reason.
 * Driver actions may come late from the phone's offline queue (EX-01): the device time is
 * used only when it is plausible, and every effective time is recorded. */
public class DeliveryManager : DomainService
{
    private static readonly TimeSpan MaxClockAhead = TimeSpan.FromMinutes(2);

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IncidentManager _incidentManager;
    private readonly OutboxManager _outbox;
    private readonly IStringEncryptionService _encryption;
    private readonly ISettingProvider _settingProvider;
    private readonly IRepository<ContactAttempt, Guid> _contactRepository;
    private readonly IRepository<CustomerProfile, Guid> _customerProfileRepository;

    public DeliveryManager(
        IRepository<Order, Guid> orderRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Driver, Guid> driverRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IncidentManager incidentManager,
        OutboxManager outbox,
        IStringEncryptionService encryption,
        ISettingProvider settingProvider,
        IRepository<ContactAttempt, Guid> contactRepository,
        IRepository<CustomerProfile, Guid> customerProfileRepository)
    {
        _contactRepository = contactRepository;
        _customerProfileRepository = customerProfileRepository;
        _orderRepository = orderRepository;
        _assignmentRepository = assignmentRepository;
        _driverRepository = driverRepository;
        _restaurantRepository = restaurantRepository;
        _incidentManager = incidentManager;
        _outbox = outbox;
        _encryption = encryption;
        _settingProvider = settingProvider;
    }

    /* EX-01: a device time is accepted if it is not before the previous step and not later than
     * the server time by more than 2 minutes; otherwise the server time is used. */
    public DateTime ResolveEventTime(DateTime? clientOccurredAt, DateTime lastStepAt, DateTime now)
    {
        return clientOccurredAt.HasValue && clientOccurredAt.Value >= lastStepAt && clientOccurredAt.Value <= now + MaxClockAhead
            ? clientOccurredAt.Value
            : now;
    }

    public async Task ArrivedAtRestaurantAsync(Order order, DeliveryAssignment assignment, Driver driver, DateTime? clientOccurredAt)
    {
        var restaurant = await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
        var radius = await _settingProvider.GetAsync<int>(SahamSettings.Drivers.ArrivedAtRestaurantRadiusMeters);
        if (driver.LastLocation == null)
        {
            throw new BusinessException(SahamErrorCodes.NoDriverLocation);
        }

        var distance = GeoFactory.DistanceMeters(driver.LastLocation, restaurant.Location);
        if (distance > radius)
        {
            throw new BusinessException(SahamErrorCodes.TooFarFromRestaurant)
                .WithData("distance", (int)Math.Round(distance))
                .WithData("radius", radius);
        }

        var eventTime = ResolveEventTime(clientOccurredAt, order.LastStepAt, Clock.Now);
        assignment.MarkArrivedAtRestaurant(eventTime);
        order.RecordEvent(OrderEventType.ArrivedAtRestaurant, ActorType.Driver, driver.Id, null, eventTime, clientOccurredAt, GuidGenerator.Create);
    }

    /* T-12 / T-13. A fresh 4-digit code goes by SMS to the recipient (or the customer) with the
     * "only give it after you receive your order" warning (EX-13). Only its hash is checked;
     * an encrypted copy lets the customer's tracking screen show it. */
    public async Task PickUpAsync(Order order, DeliveryAssignment assignment, PickedUpByType by, Guid userId, DateTime? clientOccurredAt)
    {
        var eventTime = by == PickedUpByType.Driver
            ? ResolveEventTime(clientOccurredAt, order.LastStepAt, Clock.Now)
            : Clock.Now;

        var code = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        order.MarkPickedUp(
            by == PickedUpByType.Driver ? ActorType.Driver : ActorType.Restaurant,
            userId,
            eventTime,
            by == PickedUpByType.Driver ? clientOccurredAt : null,
            HashCode(order.Id, code),
            _encryption.Encrypt(code)!,
            GuidGenerator.Create);
        assignment.MarkPickedUp(by, eventTime);
        // The SMS with the code goes through the notification queue (ORDER_PICKED_UP → NotificationPlanner),
        // with retries and an incident if it finally fails (REQ-NTF-02).
    }

    /* BR-074: the cashier's "handed to the driver". If the driver has not recorded the pickup
     * yet, this records it; otherwise it is the second confirmation. */
    public async Task HandoverByCashierAsync(Order order, Guid cashierUserId)
    {
        if (order.Status == OrderStatus.PickedUp)
        {
            order.RecordRestaurantHandover(cashierUserId, Clock.Now, GuidGenerator.Create);
            return;
        }

        var assignment = await FindActiveAssignmentAsync(order.Id)
                         ?? throw new BusinessException(SahamErrorCodes.NoActiveAssignment);
        await PickUpAsync(order, assignment, PickedUpByType.Cashier, cashierUserId, null);
    }

    public void ArrivedAtCustomer(Order order, DeliveryAssignment assignment, Guid driverId, DateTime? clientOccurredAt)
    {
        var eventTime = ResolveEventTime(clientOccurredAt, order.LastStepAt, Clock.Now);
        assignment.MarkArrivedAtCustomer(eventTime);
        order.RecordEvent(OrderEventType.ArrivedAtCustomer, ActorType.Driver, driverId, null, eventTime, clientOccurredAt, GuidGenerator.Create);
    }

    /* The driver types the code. A wrong code is counted (not an error, so the count is saved);
     * at the limit the code locks and ops are alerted (BR-034, OTP_LOCKED). */
    public async Task<DeliveryAttemptResult> DeliverWithCodeAsync(
        Order order, DeliveryAssignment assignment, Driver driver, string code, DateTime? clientOccurredAt,
        long changeShortfall = 0)
    {
        if (order.Status != OrderStatus.PickedUp)
        {
            throw new BusinessException(SahamErrorCodes.InvalidStateTransition)
                .WithData("from", order.Status.ToString()).WithData("to", nameof(OrderStatus.Delivered));
        }

        if (order.OtpLocked)
        {
            throw new BusinessException(SahamErrorCodes.DeliveryCodeLocked);
        }

        var maxAttempts = await _settingProvider.GetAsync<int>(SahamSettings.Delivery.OtpMaxAttempts);
        if (order.DeliveryOtpHash == null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(order.DeliveryOtpHash),
                Encoding.ASCII.GetBytes(HashCode(order.Id, code.Trim()))))
        {
            var locked = order.RegisterOtpFailure(maxAttempts);
            if (locked)
            {
                await _incidentManager.OpenAsync(IncidentType.DeliveryCodeLocked, IncidentSeverity.High, order.Id,
                    orderId: order.Id, driverId: driver.Id, restaurantId: order.RestaurantId);
            }

            return new DeliveryAttemptResult(false, locked, Math.Max(0, maxAttempts - order.OtpFailedAttempts));
        }

        if (changeShortfall > 0)
        {
            order.RecordChangeShortfall(changeShortfall); // EX-14: credited to the customer on posting
        }

        var eventTime = ResolveEventTime(clientOccurredAt, order.LastStepAt, Clock.Now);
        await CompleteAsync(order, assignment, driver, DeliveryProofMethod.Otp, ActorType.Driver, driver.Id, null, eventTime, clientOccurredAt);
        return new DeliveryAttemptResult(true, false, 0);
    }

    /* DER-CUS-08: the customer's own "I received my order", only once the order has left the restaurant. */
    public async Task ConfirmByCustomerAsync(Order order)
    {
        var (assignment, driver) = await GetActiveAsync(order);
        await CompleteAsync(order, assignment, driver, DeliveryProofMethod.CustomerConfirm, ActorType.Customer,
            order.CustomerId, null, Clock.Now, null);
    }

    /* order.force_deliver ✱: ops confirm "delivered without code" after reaching the customer;
     * who confirmed it and why is stored (EX-02, Appendix C §4). */
    public async Task ForceDeliverAsync(Order order, string reason, Guid staffUserId)
    {
        var (assignment, driver) = await GetActiveAsync(order);
        await CompleteAsync(order, assignment, driver, DeliveryProofMethod.OpsOverride, ActorType.Staff,
            staffUserId, reason, Clock.Now, null);
    }

    /* BR-063: after waiting N minutes at the address the driver reports the customer is not answering;
     * ops call the customer and decide "not received" (phase 7) or another outcome. */
    public async Task ReportCustomerNotAnsweringAsync(Order order, DeliveryAssignment assignment, Guid driverId)
    {
        var wait = await _settingProvider.GetAsync<int>(SahamSettings.Delivery.NoShowWaitMinutes);
        var now = Clock.Now;
        if (assignment.ArrivedAtCustomerAt == null || assignment.ArrivedAtCustomerAt.Value > now.AddMinutes(-wait))
        {
            throw new BusinessException(SahamErrorCodes.NoShowTooEarly).WithData("minutes", wait);
        }

        await _incidentManager.OpenAsync(IncidentType.CustomerNotAnswering, IncidentSeverity.High, order.Id,
            orderId: order.Id, driverId: driverId, restaurantId: order.RestaurantId);
        order.RecordEvent(OrderEventType.CustomerNotAnswering, ActorType.Driver, driverId, null, now, null, GuidGenerator.Create);
    }

    /* order.mark_not_received ✱ (BR-063, DEC-06): only after at least one unanswered call.
     * The restaurant and the driver are paid in full, the cash amount becomes the customer's debt
     * (posted by LedgerOrderEventHandler), the no-show counter grows and two no-shows disable cash
     * (BR-065). The food stays with the driver. */
    public async Task MarkNotReceivedAsync(Order order, Guid staffUserId, string? note)
    {
        if (!await _contactRepository.AnyAsync(c => c.OrderId == order.Id &&
                (c.Outcome == ContactOutcome.NoAnswer || c.Outcome == ContactOutcome.CustomerOffline)))
        {
            throw new BusinessException(SahamErrorCodes.ContactAttemptRequired);
        }

        var (assignment, driver) = await GetActiveAsync(order, nameof(OrderStatus.DeliveryFailed));
        var now = Clock.Now;
        order.MarkDeliveryFailed(FailureReason.CustomerNoShow, note, staffUserId, now, GuidGenerator.Create);
        await FinishFailedAsync(assignment, driver, now);

        var profile = await _customerProfileRepository.FindAsync(order.CustomerId);
        if (profile == null)
        {
            profile = await _customerProfileRepository.InsertAsync(new CustomerProfile(order.CustomerId));
        }

        var wasCashDisabled = profile.CashDisabled;
        profile.RegisterNoShow(await _settingProvider.GetAsync<int>(SahamSettings.Delivery.NoShowCashBanThreshold), now);
        await _customerProfileRepository.UpdateAsync(profile);
        if (profile.CashDisabled && !wasCashDisabled)
        {
            // CUSTOMER_CASH_BANNED: push + SMS to the customer (I.2-6).
            await _outbox.EnqueueAsync(OutboxEventTypes.CustomerCashBanned, OutboxAggregateTypes.Customer, order.CustomerId,
                new Dictionary<string, string?> { ["orderId"] = order.Id.ToString() }, order.Id.ToString("N"));
        }
        await _incidentManager.AutoResolveAsync(IncidentType.CustomerNotAnswering, order.Id, "NOT_RECEIVED");
    }

    /* EX-02 at +45 min: the driver never arrived and cannot be reached. The customer pays nothing
     * (no debt), the restaurant gets its net from the company (J-DRIVER-INCIDENT), no driver pay. */
    public async Task MarkDriverIncidentAsync(Order order, Guid staffUserId, string note)
    {
        var (assignment, driver) = await GetActiveAsync(order, nameof(OrderStatus.DeliveryFailed));
        var now = Clock.Now;
        order.MarkDeliveryFailed(FailureReason.DriverIncident, note, staffUserId, now, GuidGenerator.Create);
        await FinishFailedAsync(assignment, driver, now);
        await _incidentManager.AutoResolveAsync(IncidentType.DriverStale, driver.Id, "DRIVER_INCIDENT");
    }

    private async Task FinishFailedAsync(DeliveryAssignment assignment, Driver driver, DateTime now)
    {
        assignment.MarkFailed(now);
        driver.RemoveActiveOrder(now, completed: true);
        await _assignmentRepository.UpdateAsync(assignment);
        await _driverRepository.UpdateAsync(driver);
    }

    public async Task<DeliveryAssignment?> FindActiveAssignmentAsync(Guid orderId)
    {
        return await _assignmentRepository.FindAsync(a =>
            a.OrderId == orderId && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));
    }

    private async Task CompleteAsync(Order order, DeliveryAssignment assignment, Driver driver, DeliveryProofMethod proof,
        ActorType actorType, Guid? by, string? overrideReason, DateTime eventTime, DateTime? clientOccurredAt)
    {
        order.MarkDelivered(proof, actorType, by, overrideReason, eventTime, clientOccurredAt, GuidGenerator.Create);

        // BR-035: compare where the driver is with the address; flag, never block.
        var geofence = await _settingProvider.GetAsync<int>(SahamSettings.Drivers.DeliveryGeofenceMeters);
        var far = driver.LastLocation != null && GeoFactory.DistanceMeters(driver.LastLocation, order.DeliveryLocation) > geofence;
        assignment.MarkDelivered(eventTime, driver.LastLocation, far);
        if (far && proof == DeliveryProofMethod.Otp)
        {
            await _incidentManager.OpenAsync(IncidentType.DeliveredFarFromAddress, IncidentSeverity.Low, order.Id,
                orderId: order.Id, driverId: driver.Id);
        }

        driver.RemoveActiveOrder(eventTime, completed: true);

        await _assignmentRepository.UpdateAsync(assignment);
        await _driverRepository.UpdateAsync(driver);
        await _incidentManager.AutoResolveAsync(IncidentType.DeliveryCodeLocked, order.Id, "DELIVERED");
        await _incidentManager.AutoResolveAsync(IncidentType.CustomerNotAnswering, order.Id, "DELIVERED");
        await _incidentManager.AutoResolveAsync(IncidentType.DriverStale, driver.Id, "DELIVERED");
    }

    private async Task<(DeliveryAssignment, Driver)> GetActiveAsync(Order order, string target = nameof(OrderStatus.Delivered))
    {
        if (order.Status != OrderStatus.PickedUp)
        {
            throw new BusinessException(SahamErrorCodes.InvalidStateTransition)
                .WithData("from", order.Status.ToString()).WithData("to", target);
        }

        var assignment = await FindActiveAssignmentAsync(order.Id)
                         ?? throw new BusinessException(SahamErrorCodes.NoActiveAssignment);
        return (assignment, await _driverRepository.GetAsync(assignment.DriverId));
    }

    private static string HashCode(Guid orderId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId:N}:{code}")));

    /* For the customer's tracking screen only. */
    public string? RevealCode(Order order) =>
        order.Status == OrderStatus.PickedUp && order.DeliveryOtpProtected != null
            ? _encryption.Decrypt(order.DeliveryOtpProtected)
            : null;
}

public record DeliveryAttemptResult(bool Delivered, bool Locked, int AttemptsLeft);
