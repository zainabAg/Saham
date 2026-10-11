using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Services.Dtos.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Orders;

/* One place that decides which order fields each audience sees:
 * customer, restaurant (no phone, no full address — DER-RST-09) and ops. */
public class OrderDtoMapper : ITransientDependency
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IRepository<Entities.Payments.Payment, Guid> _paymentRepository;

    public OrderDtoMapper(IRepository<Restaurant, Guid> restaurantRepository, IRepository<IdentityUser, Guid> userRepository,
        IRepository<Entities.Payments.Payment, Guid> paymentRepository)
    {
        _paymentRepository = paymentRepository;
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
    }

    public async Task<List<CustomerOrderDto>> ToCustomerAsync(IReadOnlyCollection<Order> orders)
    {
        var names = await GetRestaurantNamesAsync(orders);
        var orderIds = orders.Where(o => o.PaymentMethod == PaymentMethod.Wallet).Select(o => o.Id).ToList();
        var payments = (await _paymentRepository.GetListAsync(p => orderIds.Contains(p.OrderId)))
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreationTime).First());
        return orders.Select(o => new CustomerOrderDto
        {
            Id = o.Id,
            Number = o.Number,
            CouponCode = o.CouponCode,
            Payment = payments.TryGetValue(o.Id, out var p)
                ? new OrderPaymentDto
                {
                    PaymentId = p.Id,
                    Status = p.Status,
                    Amount = p.Amount,
                    RedirectUrl = p.IsOpen ? p.RedirectUrl : null,
                    ExpiresAt = p.ExpiresAt,
                    RefundedAmount = p.RefundedAmount
                }
                : null,
            Status = o.Status,
            HoldReason = o.HoldReason,
            RestaurantId = o.RestaurantId,
            RestaurantName = names.GetValueOrDefault(o.RestaurantId, string.Empty),
            OrderType = o.OrderType,
            ScheduledFor = o.ScheduledFor,
            DeliveryDescription = o.DeliveryDescription,
            RecipientName = o.RecipientName,
            Items = Lines(o),
            ItemsSubtotal = o.ItemsSubtotal,
            DiscountAmount = o.DiscountAmount,
            FreeDeliveryApplied = o.FreeDeliveryApplied,
            DeliveryFee = o.DeliveryFeeCharged,
            TotalAmount = o.TotalAmount,
            DebtCollected = o.DebtCollected,
            CreditApplied = o.CreditApplied,
            AmountDue = o.AmountDue,
            PaymentMethod = o.PaymentMethod,
            CashTendered = o.CashTendered,
            ChangeDue = o.CashTendered.HasValue ? o.CashTendered.Value - o.AmountDue : null,
            CreationTime = o.CreationTime,
            AcceptedAt = o.AcceptedAt,
            ReadyAt = o.ReadyAt,
            CancelledAt = o.CancelledAt,
            EtaMinAt = o.EtaMinAt,
            EtaMaxAt = o.EtaMaxAt,
            CancelReason = o.CancelReason,
            ItemDecisionDeadlineAt = o.ItemDecisionDeadlineAt,
            CanCancel = o.Status is OrderStatus.PendingPayment or OrderStatus.Scheduled or OrderStatus.Placed
        }).ToList();
    }

    public async Task<List<RestaurantOrderDto>> ToRestaurantAsync(IReadOnlyCollection<Order> orders)
    {
        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var users = (await _userRepository.GetListAsync(u => customerIds.Contains(u.Id))).ToDictionary(u => u.Id);

        return orders.Select(o => new RestaurantOrderDto
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            HoldReason = o.HoldReason,
            CustomerFirstName = FirstName(users.GetValueOrDefault(o.CustomerId)?.Name),
            OrderType = o.OrderType,
            ScheduledFor = o.ScheduledFor,
            Items = Lines(o),
            ItemsSubtotal = o.ItemsSubtotal,
            RestaurantDiscount = o.RestaurantDiscount,
            CommissionAmount = o.CommissionAmount,
            RestaurantNetAmount = o.RestaurantNetAmount,
            PrepMinutes = o.PrepMinutes,
            PlacedAt = o.PlacedAt,
            AcceptedAt = o.AcceptedAt,
            EstimatedReadyAt = o.EstimatedReadyAt,
            ReadyAt = o.ReadyAt,
            CancelReason = o.CancelReason,
            CancelNote = o.CancelNote,
            ItemDecisionDeadlineAt = o.ItemDecisionDeadlineAt
        }).ToList();
    }

    public async Task<List<OpsOrderDto>> ToOpsAsync(IReadOnlyCollection<Order> orders, bool includeHistory)
    {
        var names = await GetRestaurantNamesAsync(orders);
        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var users = (await _userRepository.GetListAsync(u => customerIds.Contains(u.Id))).ToDictionary(u => u.Id);

        return orders.Select(o => new OpsOrderDto
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            HoldReason = o.HoldReason,
            DispatchStatus = o.DispatchStatus,
            RestaurantId = o.RestaurantId,
            RestaurantName = names.GetValueOrDefault(o.RestaurantId, string.Empty),
            CustomerId = o.CustomerId,
            CustomerName = users.GetValueOrDefault(o.CustomerId)?.Name,
            OrderType = o.OrderType,
            ScheduledFor = o.ScheduledFor,
            ReleaseAt = o.ReleaseAt,
            DeliveryDescription = o.DeliveryDescription,
            RecipientName = o.RecipientName,
            DistanceM = o.DistanceM,
            Items = Lines(o),
            ItemsSubtotal = o.ItemsSubtotal,
            DiscountAmount = o.DiscountAmount,
            DeliveryFee = o.DeliveryFeeCharged,
            TotalAmount = o.TotalAmount,
            CommissionAmount = o.CommissionAmount,
            RestaurantNetAmount = o.RestaurantNetAmount,
            DriverPayAmount = o.DriverPayAmount,
            AmountDue = o.AmountDue,
            PaymentMethod = o.PaymentMethod,
            CashTendered = o.CashTendered,
            CreationTime = o.CreationTime,
            PlacedAt = o.PlacedAt,
            AcceptedAt = o.AcceptedAt,
            EstimatedReadyAt = o.EstimatedReadyAt,
            EtaMaxAt = o.EtaMaxAt,
            CancelReason = o.CancelReason,
            CancelNote = o.CancelNote,
            CancelledByType = o.CancelledByType,
            ItemDecisionDeadlineAt = o.ItemDecisionDeadlineAt,
            History = includeHistory
                ? o.History.OrderBy(h => h.OccurredAt).Select(h => new OrderHistoryDto
                {
                    EventType = h.EventType,
                    FromStatus = h.FromStatus,
                    ToStatus = h.ToStatus,
                    ActorType = h.ActorType,
                    ActorUserId = h.ActorUserId,
                    Reason = h.Reason,
                    OccurredAt = h.OccurredAt
                }).ToList()
                : new List<OrderHistoryDto>()
        }).ToList();
    }

    private static List<OrderLineDto> Lines(Order order) => order.Items
        .OrderBy(i => i.Status)
        .Select(i => new OrderLineDto
        {
            Id = i.Id,
            MenuItemId = i.MenuItemId,
            Name = i.NameSnapshot.Localize(),
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            LineTotal = i.LineTotal,
            Status = i.Status,
            IsReportedUnavailable = i.IsReportedUnavailable,
            SubstituteForItemId = i.SubstituteForItemId
        }).ToList();

    private static string FirstName(string? name) =>
        name.IsNullOrWhiteSpace() ? string.Empty : name!.Trim().Split(' ')[0];

    private async Task<Dictionary<Guid, string>> GetRestaurantNamesAsync(IReadOnlyCollection<Order> orders)
    {
        var ids = orders.Select(o => o.RestaurantId).Distinct().ToList();
        var restaurants = await _restaurantRepository.GetListAsync(r => ids.Contains(r.Id));
        return restaurants.ToDictionary(r => r.Id, r => r.Name.Localize());
    }
}
