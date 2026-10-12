using System;
using System.Linq;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Entities.Pricing;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Saham.Entities.Dispatch;

/* The dispatch engine (SRS 6-E, UC-16, WF-03). Sequential offers to one driver at a time:
 *   eligible (BR-026): approved, available, fresh location (≤120 s), free (no active order —
 *                      the second order is given by ops until batching is tuned, TD-06),
 *                      no other pending offer, never offered / released for this order,
 *                      not blocked from cash for a cash order;
 *   order    (BR-027): nearest to the restaurant (PostGIS distance), then the one waiting longest;
 *   offer    (BR-028): 30 s of server time; decline / expiry → next driver;
 *   accept   (BR-029): atomic — pending, not expired, order still dispatchable, no active
 *                      assignment, driver below capacity; the database indexes are the last guard.
 * No driver: keep retrying, open a NO_DRIVER incident after N minutes, never cancel (BR-032). */
public class DispatchManager : DomainService
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DeliveryOffer, Guid> _offerRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<PricingPlan, Guid> _planRepository;
    private readonly IncidentManager _incidentManager;
    private readonly ISettingProvider _settingProvider;

    public DispatchManager(
        IRepository<Order, Guid> orderRepository,
        IRepository<Driver, Guid> driverRepository,
        IRepository<DeliveryOffer, Guid> offerRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<PricingPlan, Guid> planRepository,
        IncidentManager incidentManager,
        ISettingProvider settingProvider)
    {
        _orderRepository = orderRepository;
        _driverRepository = driverRepository;
        _offerRepository = offerRepository;
        _assignmentRepository = assignmentRepository;
        _restaurantRepository = restaurantRepository;
        _planRepository = planRepository;
        _incidentManager = incidentManager;
        _settingProvider = settingProvider;
    }

    // ------------------------------------------------------------------ automatic offers

    /* One dispatch step for an order: create the next offer, or record that nobody is available. */
    public async Task<DeliveryOffer?> TryOfferNextAsync(Order order)
    {
        var now = Clock.Now;
        var startBefore = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.StartBeforeReadyMinutes);
        if (!order.ShouldStartDispatch(now, startBefore) ||
            await _offerRepository.AnyAsync(o => o.OrderId == order.Id && o.Status == OfferStatus.Pending) ||
            await HasActiveAssignmentAsync(order.Id))
        {
            return null;
        }

        order.StartDispatch(now);

        var restaurant = await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
        var candidate = await FindBestDriverAsync(order, restaurant.Location, now);
        if (candidate == null)
        {
            var noDriverMinutes = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.NoDriverIncidentMinutes);
            if (order.DispatchStartedAt <= now.AddMinutes(-noDriverMinutes))
            {
                await _incidentManager.OpenAsync(IncidentType.NoDriver, IncidentSeverity.High, order.Id,
                    orderId: order.Id, restaurantId: order.RestaurantId);
            }

            return null;
        }

        var timeout = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.OfferTimeoutSeconds);
        var round = await _offerRepository.CountAsync(o => o.OrderId == order.Id) + 1;
        var offer = new DeliveryOffer(GuidGenerator.Create(), order.Id, candidate.Value.DriverId, round,
            candidate.Value.DistanceM, now, now.AddSeconds(timeout));

        return await _offerRepository.InsertAsync(offer);
    }

    private async Task<(Guid DriverId, int DistanceM)?> FindBestDriverAsync(Order order, Point restaurantLocation, DateTime now)
    {
        var freshSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Drivers.LocationFreshSeconds);
        var maxActive = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.MaxActiveOrdersPerDriver);
        var isCash = order.PaymentMethod == PaymentMethod.Cash;

        var offers = await _offerRepository.GetQueryableAsync();
        var assignments = await _assignmentRepository.GetQueryableAsync();

        var query = (await _driverRepository.GetQueryableAsync())
            .Where(DriverDispatchEligibility.Predicate(order.CityId, isCash, now, freshSeconds, maxActive, automatic: true))
            // Do not trust the counter alone when selecting a supposedly idle driver.
            .Where(d => !assignments.Any(a => a.DriverId == d.Id &&
                (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp)))
            // not busy with another offer, never offered this order before
            .Where(d => !offers.Any(o => o.DriverId == d.Id && (o.Status == OfferStatus.Pending || o.OrderId == order.Id)))
            // not a driver who already held and released this order (BR-033)
            .Where(d => !assignments.Any(a => a.DriverId == d.Id && a.OrderId == order.Id))
            .OrderBy(d => d.LastLocation!.Distance(restaurantLocation))
            .ThenBy(d => d.LastTaskCompletedAt ?? DateTime.MinValue)
            .ThenBy(d => d.Id) // stable choice when distance and waiting time are equal
            .Select(d => new { d.Id, Distance = d.LastLocation!.Distance(restaurantLocation) });

        var best = await AsyncExecuter.FirstOrDefaultAsync(query);
        return best == null ? null : (best.Id, (int)Math.Round(best.Distance));
    }

    /* Offers past their 30 seconds become EXPIRED; the next worker tick offers the next driver. */
    public async Task<int> ExpireOffersAsync()
    {
        var now = Clock.Now;
        var expired = await _offerRepository.GetListAsync(o => o.Status == OfferStatus.Pending && o.ExpiresAt < now);
        foreach (var offer in expired)
        {
            offer.Expire(now);
        }

        await _offerRepository.UpdateManyAsync(expired);
        return expired.Count;
    }

    // ------------------------------------------------------------------ driver answers

    public async Task<DeliveryAssignment> AcceptOfferAsync(Guid offerId, Guid driverId)
    {
        var now = Clock.Now;
        var offer = await GetOwnOfferAsync(offerId, driverId);
        var order = await _orderRepository.GetAsync(offer.OrderId);

        if (!order.CanBeDispatched)
        {
            throw new BusinessException(SahamErrorCodes.OrderNotDispatchable).WithData("status", order.Status.ToString());
        }

        if (await HasActiveAssignmentAsync(order.Id))
        {
            throw new BusinessException(SahamErrorCodes.OrderAlreadyAssigned);
        }

        var driver = await _driverRepository.GetAsync(driverId);
        // An offer does not reserve eligibility: approval, availability, cash block and
        // location can change during its 30 seconds (BR-029).
        await EnsureEligibleAsync(order, driver, now, automatic: true);
        if (await _assignmentRepository.AnyAsync(a => a.DriverId == driverId &&
                (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp)))
        {
            throw new BusinessException(SahamErrorCodes.DriverAtCapacity);
        }

        now = Clock.Now; // do not accept against the timestamp from before database/settings reads
        offer.Accept(now); // pending and not expired, or 409
        await _offerRepository.UpdateAsync(offer);
        return await CreateAssignmentAsync(order, driver, offer.Id, AssignedByType.System, null, now);
    }

    public async Task DeclineOfferAsync(Guid offerId, Guid driverId)
    {
        var offer = await GetOwnOfferAsync(offerId, driverId);
        offer.Decline(Clock.Now);
        await _offerRepository.UpdateAsync(offer);
    }

    // ------------------------------------------------------------------ ops

    /* dispatch.manual_assign ✱: ops give the order to a driver, e.g. a second order on the way
     * (BR-031) or when nobody accepts. An existing assignment before pickup is released first. */
    public async Task<DeliveryAssignment> AssignManuallyAsync(Order order, Guid driverId, string reason, Guid staffUserId)
    {
        if (reason.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.ReasonRequired);
        }

        var now = Clock.Now;
        var driver = await _driverRepository.GetAsync(driverId);

        // The order's own state (Placed/Accepted/Ready, not on hold) is checked by MarkAssigned.
        // Validate before releasing the old assignment, so an ineligible replacement
        // cannot take away the order's current driver.
        await EnsureEligibleAsync(order, driver, now, automatic: false);

        var active = await FindActiveAssignmentAsync(order.Id);
        if (active?.DriverId == driverId)
        {
            throw new BusinessException(SahamErrorCodes.OrderAlreadyAssigned);
        }

        if (active != null)
        {
            await ReleaseAsync(order, active, "REASSIGNED: " + reason, ActorType.Staff, staffUserId, restartDispatch: false);
        }

        await CancelPendingOffersAsync(order.Id);
        return await CreateAssignmentAsync(order, driver, null, AssignedByType.Staff, staffUserId, now);
    }

    /* BR-033: the driver gives up, or ops take the order away, before pickup. */
    public async Task ReleaseActiveAsync(Order order, string reason, ActorType actorType, Guid? by)
    {
        var active = await FindActiveAssignmentAsync(order.Id)
                     ?? throw new BusinessException(SahamErrorCodes.NoActiveAssignment);
        await ReleaseAsync(order, active, reason, actorType, by, restartDispatch: true);
    }

    // ------------------------------------------------------------------ reactions to order changes

    /* Order cancelled: no offer or assignment may remain (INV-04); the driver is freed. */
    public async Task StopForOrderAsync(Order order)
    {
        await CancelPendingOffersAsync(order.Id);

        var active = await FindActiveAssignmentAsync(order.Id);
        if (active is { Status: AssignmentStatus.Assigned })
        {
            await ReleaseAsync(order, active, "ORDER_CANCELLED", ActorType.System, null, restartDispatch: false);
        }

        await _incidentManager.AutoResolveAsync(IncidentType.NoDriver, order.Id, "ORDER_CANCELLED");
    }

    /* Order on hold (item unavailable): pending offers are cancelled, an assignment stays (UC-12). */
    public Task PauseForHoldAsync(Order order) => CancelPendingOffersAsync(order.Id);

    // ------------------------------------------------------------------ helpers

    public async Task<DeliveryAssignment?> FindActiveAssignmentAsync(Guid orderId)
    {
        return await _assignmentRepository.FindAsync(a =>
            a.OrderId == orderId && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));
    }

    private Task<bool> HasActiveAssignmentAsync(Guid orderId) =>
        _assignmentRepository.AnyAsync(a =>
            a.OrderId == orderId && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));

    private async Task EnsureEligibleAsync(Order order, Driver driver, DateTime now, bool automatic)
    {
        var freshSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Drivers.LocationFreshSeconds);
        var maxActive = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.MaxActiveOrdersPerDriver);
        DriverDispatchEligibility.Ensure(driver, order.CityId, order.PaymentMethod == PaymentMethod.Cash,
            now, freshSeconds, maxActive, automatic);
    }

    private async Task<DeliveryAssignment> CreateAssignmentAsync(
        Order order, Driver driver, Guid? offerId, AssignedByType byType, Guid? byUser, DateTime now)
    {
        var maxActive = await _settingProvider.GetAsync<int>(SahamSettings.Dispatch.MaxActiveOrdersPerDriver);
        var isBatch = driver.ActiveOrderCount > 0;

        // BR-017: base + distance extra; a second order on the same trip gets the fixed batch pay.
        var plan = await _planRepository.GetAsync(order.PricingPlanId);
        var tier = plan.FindTier(order.DistanceM);
        var pay = isBatch ? plan.BatchSecondOrderPay : plan.DriverBasePay + (tier?.DriverExtra ?? 0);

        driver.AddActiveOrder(maxActive); // 409 at capacity; CHECK (≤ 2) in the database as well
        await _driverRepository.UpdateAsync(driver);

        var assignment = new DeliveryAssignment(GuidGenerator.Create(), order.Id, driver.Id, offerId, byType, byUser, isBatch, pay, now);
        await _assignmentRepository.InsertAsync(assignment);

        order.MarkAssigned(driver.Id, pay, byType == AssignedByType.Staff ? ActorType.Staff : ActorType.Driver,
            byUser ?? driver.Id, now, GuidGenerator.Create);
        await _orderRepository.UpdateAsync(order);

        await _incidentManager.AutoResolveAsync(IncidentType.NoDriver, order.Id, "ASSIGNED");
        return assignment;
    }

    private async Task ReleaseAsync(Order order, DeliveryAssignment assignment, string reason, ActorType actorType, Guid? by, bool restartDispatch)
    {
        if (assignment.Status != AssignmentStatus.Assigned)
        {
            // After pickup the order cannot change hands; only "not received" / failure (BR-033).
            throw new BusinessException(SahamErrorCodes.AssignmentAlreadyPickedUp);
        }

        var now = Clock.Now;
        assignment.Release(reason, now);
        await _assignmentRepository.UpdateAsync(assignment);

        var driver = await _driverRepository.GetAsync(assignment.DriverId);
        driver.RemoveActiveOrder(now, completed: false);
        await _driverRepository.UpdateAsync(driver);

        if (restartDispatch || order.Status != OrderStatus.Cancelled)
        {
            order.MarkAssignmentReleased(reason, actorType, by, now, GuidGenerator.Create);
        }
    }

    private async Task CancelPendingOffersAsync(Guid orderId)
    {
        var now = Clock.Now;
        var pending = await _offerRepository.GetListAsync(o => o.OrderId == orderId && o.Status == OfferStatus.Pending);
        foreach (var offer in pending)
        {
            offer.Cancel(now);
        }

        await _offerRepository.UpdateManyAsync(pending);
    }

    /* Another driver's offer is "not found". */
    private async Task<DeliveryOffer> GetOwnOfferAsync(Guid offerId, Guid driverId)
    {
        var offer = await _offerRepository.GetAsync(offerId);
        if (offer.DriverId != driverId)
        {
            throw new EntityNotFoundException(typeof(DeliveryOffer), offerId);
        }

        return offer;
    }
}
