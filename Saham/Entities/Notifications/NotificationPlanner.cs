using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Dispatch;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Settings;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Entities.Notifications;

/* SRS NotificationService.onEvent: who hears about an event and on which channel — the
 * notification matrix I.2-6. One outbox event becomes zero or more Notification rows; a row that
 * already exists for (event, channel, user) is skipped, so publishing twice is harmless.
 * Realtime (SignalR) and ops incidents are sent elsewhere; this is PUSH and SMS only. */
public class NotificationPlanner : DomainService
{
    private readonly IRepository<Notification, Guid> _notificationRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<RestaurantStaff, Guid> _staffRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly ISettingProvider _settingProvider;

    public NotificationPlanner(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<Order, Guid> orderRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<RestaurantStaff, Guid> staffRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<City, Guid> cityRepository,
        IRepository<IdentityUser, Guid> userRepository,
        ISettingProvider settingProvider)
    {
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _restaurantRepository = restaurantRepository;
        _staffRepository = staffRepository;
        _assignmentRepository = assignmentRepository;
        _cityRepository = cityRepository;
        _userRepository = userRepository;
        _settingProvider = settingProvider;
    }

    public async Task<int> PlanAsync(OutboxEvent e)
    {
        var plans = e.AggregateType == OutboxAggregateTypes.Order
            ? await PlanOrderEventAsync(e)
            : await PlanOtherEventAsync(e);

        var created = 0;
        foreach (var plan in plans.Where(p => p.Channel == NotificationChannel.Push || !p.Phone.IsNullOrWhiteSpace()))
        {
            if (await _notificationRepository.AnyAsync(n =>
                    n.EventKey == e.EventKey && n.Channel == plan.Channel && n.UserId == plan.UserId))
            {
                continue;
            }

            await _notificationRepository.InsertAsync(new Notification(GuidGenerator.Create(), e.EventKey, plan.Channel,
                plan.UserId, plan.Phone, plan.Template, plan.Args, plan.OrderId, plan.HighPriority, plan.ExpiresAt, Clock.Now));
            created++;
        }

        return created;
    }

    // ------------------------------------------------------------------ order events

    private async Task<List<Plan>> PlanOrderEventAsync(OutboxEvent e)
    {
        var order = await _orderRepository.GetAsync(e.AggregateId, includeDetails: false);
        var restaurant = await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
        var number = order.Number.ToString(CultureInfo.InvariantCulture);
        var name = restaurant.Name.Ar;
        var customer = order.CustomerId;
        var plans = new List<Plan>();

        switch (e.EventType)
        {
            case OutboxEventTypes.OrderPlaced:
                // The tablet already rings over realtime; the push wakes a tablet whose app is in the background.
                foreach (var staffUserId in await GetRestaurantStaffAsync(order.RestaurantId))
                {
                    plans.Add(Push(staffUserId, "Notify:NewOrder", order.Id, true, number));
                }

                plans.Add(Push(customer, "Notify:OrderSentToRestaurant", order.Id, false, number, name));
                break;

            case OutboxEventTypes.OrderAccepted:
                plans.Add(Push(customer, "Notify:OrderAccepted", order.Id, false, number, name, await LocalTimeAsync(order, order.EtaMaxAt)));
                break;

            case OutboxEventTypes.OrderRejected:
                plans.Add(Push(customer, "Notify:OrderRejected", order.Id, false, number, name));
                plans.Add(await SmsToCustomerAsync(order, "Notify:OrderRejected", number, name));
                break;

            case OutboxEventTypes.OrderCancelled:
                plans.Add(Push(customer, "Notify:OrderCancelled", order.Id, false, number, name));
                plans.Add(await SmsToCustomerAsync(order, "Notify:OrderCancelled", number, name));
                break;

            case OutboxEventTypes.OrderReady:
                var assignment = await _assignmentRepository.FirstOrDefaultAsync(a => a.OrderId == order.Id &&
                    (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));
                if (assignment != null)
                {
                    plans.Add(Push(assignment.DriverId, "Notify:OrderReady", order.Id, false, number, name));
                }

                break;

            case OutboxEventTypes.OrderPickedUp:
                plans.Add(Push(customer, "Notify:OrderPickedUp", order.Id, false, number, name));
                // REQ-NTF-02: the delivery code by SMS is mandatory, to the named recipient if any.
                // The code itself is added only when the message is sent (NotificationTextRenderer).
                var phone = order.RecipientPhone ?? await GetPhoneAsync(customer);
                plans.Add(Sms(customer, phone, NotificationTemplates.DeliveryCode, order.Id, name, number));
                break;

            case OutboxEventTypes.OrderDelivered:
                plans.Add(Push(customer, "Notify:OrderDelivered", order.Id, false, number, name));
                break;

            case OutboxEventTypes.OrderNotReceived:
                var debt = order.PaymentMethod == PaymentMethod.Cash ? order.AmountDue : 0;
                plans.Add(Push(customer, "Notify:OrderNotReceived", order.Id, false, number, Amount(debt)));
                plans.Add(await SmsToCustomerAsync(order, "Notify:OrderNotReceived", number, Amount(debt)));
                break;

            case OutboxEventTypes.OrderLate:
                plans.Add(Push(customer, "Notify:OrderLate", order.Id, false, number, name));
                break;

            case OutboxEventTypes.ItemUnavailable:
                var minutes = (await _settingProvider.GetAsync<int>(SahamSettings.Orders.ItemDecisionMinutes)).ToString(CultureInfo.InvariantCulture);
                plans.Add(Push(customer, "Notify:ItemUnavailable", order.Id, false, number, name, minutes));
                plans.Add(await SmsToCustomerAsync(order, "Notify:ItemUnavailable", number, name, minutes));
                break;

            case OutboxEventTypes.DriverAssigned:
                plans.Add(Push(customer, "Notify:DriverAssigned", order.Id, false, number));
                break;

            case OutboxEventTypes.RefundSucceeded:
                var refunded = Amount(long.TryParse(e.Get("amount"), out var r) ? r : 0);
                plans.Add(Push(customer, "Notify:RefundSucceeded", order.Id, false, number, refunded));
                plans.Add(await SmsToCustomerAsync(order, "Notify:RefundSucceeded", number, refunded));
                break;

            case OutboxEventTypes.RefundFailedFinal:
                plans.Add(Push(customer, "Notify:RefundInProgress", order.Id, false, number));
                break;
        }

        return plans;
    }

    // ------------------------------------------------------------------ driver / customer events

    private async Task<List<Plan>> PlanOtherEventAsync(OutboxEvent e)
    {
        var plans = new List<Plan>();
        var orderId = Guid.TryParse(e.Get("orderId"), out var parsed) ? parsed : (Guid?)null;
        var order = orderId.HasValue ? await _orderRepository.FindAsync(orderId.Value, includeDetails: false) : null;
        var number = order?.Number.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        switch (e.EventType)
        {
            case OutboxEventTypes.DeliveryOffer:
                var restaurant = order == null ? null : await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
                DateTime? expiresAt = DateTime.TryParse(e.Get("expiresAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                    ? at
                    : null;
                plans.Add(Push(e.AggregateId, "Notify:DeliveryOffer", orderId, true, restaurant?.Name.Ar ?? string.Empty) with
                {
                    ExpiresAt = expiresAt
                });
                break;

            case OutboxEventTypes.TaskAssigned:
                plans.Add(Push(e.AggregateId, "Notify:TaskAssigned", orderId, true, number));
                break;

            case OutboxEventTypes.TaskReleased:
                var template = order?.Status == OrderStatus.Cancelled ? "Notify:TaskCancelled" : "Notify:TaskReleased";
                plans.Add(Push(e.AggregateId, template, orderId, false, number));
                break;

            case OutboxEventTypes.CustomerCashBanned:
                plans.Add(Push(e.AggregateId, "Notify:CashBanned", null, false));
                plans.Add(Sms(e.AggregateId, await GetPhoneAsync(e.AggregateId), "Notify:CashBanned", null));
                break;

            case OutboxEventTypes.DriverCashOverdue:
                plans.Add(Push(e.AggregateId, "Notify:DriverCashOverdue", null, false));
                break;

            case OutboxEventTypes.DriverPayoutPaid:
                plans.Add(Push(e.AggregateId, "Notify:DriverPayoutPaid", null, false,
                    Amount(long.TryParse(e.Get("amount"), out var paid) ? paid : 0)));
                break;

            case OutboxEventTypes.DriverReviewed:
                var reviewTemplate = e.Get("approved") == bool.TrueString ? "Notify:DriverApproved" : "Notify:DriverRejected";
                var note = e.Get("note") ?? string.Empty;
                plans.Add(Push(e.AggregateId, reviewTemplate, null, false, note));
                plans.Add(Sms(e.AggregateId, await GetPhoneAsync(e.AggregateId), reviewTemplate, null, note));
                break;
        }

        return plans;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<List<Guid>> GetRestaurantStaffAsync(Guid restaurantId)
    {
        var staff = await _staffRepository.GetListAsync(s => s.RestaurantId == restaurantId && s.IsActive);
        return staff.Select(s => s.UserId).Distinct().ToList();
    }

    private async Task<string?> GetPhoneAsync(Guid userId) => (await _userRepository.FindAsync(userId))?.PhoneNumber;

    private async Task<Plan> SmsToCustomerAsync(Order order, string template, params string[] args) =>
        Sms(order.CustomerId, await GetPhoneAsync(order.CustomerId), template, order.Id, args);

    private async Task<string> LocalTimeAsync(Order order, DateTime utc)
    {
        var city = await _cityRepository.GetAsync(order.CityId);
        return city.ToLocalTime(utc).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Amount(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    private static Plan Push(Guid userId, string template, Guid? orderId, bool high, params string[] args) =>
        new(NotificationChannel.Push, userId, null, template, args, orderId, high, null);

    /* A recipient without a phone number gets no SMS (the plan is dropped in PlanAsync). */
    private static Plan Sms(Guid userId, string? phone, string template, Guid? orderId, params string[] args) =>
        new(NotificationChannel.Sms, userId, phone, template, args, orderId, false, null);

    private record Plan(NotificationChannel Channel, Guid? UserId, string? Phone, string Template, string[] Args,
        Guid? OrderId, bool HighPriority, DateTime? ExpiresAt);
}

public static class NotificationTemplates
{
    /* The SMS that carries the delivery code (REQ-NTF-02): its final failure opens an incident. */
    public const string DeliveryCode = "Sms:DeliveryCode";
}
