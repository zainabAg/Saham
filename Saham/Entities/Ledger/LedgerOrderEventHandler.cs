using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Coupons;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Entities.Payments;
using Saham.Entities.Pricing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;

namespace Saham.Entities.Ledger;

/* Posts the money of an order in the same transaction as its change (SRS 14.6:
 * "delivery: order, assignment, counter, cash payment, revenue and pay entries — one transaction"):
 *   Delivered                     → J-CASH-DELIVERED or J-WALLET-DELIVERED + J-DRIVER-PAY
 *                                   (+ J-CHANGE-SHORTFALL), cash cap check; coupon consumed;
 *   DeliveryFailed / no-show      → J-NOT-RECEIVED (cash: a debt; wallet: no refund) + J-DRIVER-PAY; coupon consumed;
 *   DeliveryFailed / driver lost  → J-DRIVER-INCIDENT (no pay); wallet refunded; coupon released;
 *   Cancelled                     → J-CANCEL-DRIVER-PAY if the driver had reached the restaurant (BR-069),
 *                                   J-CUSTOMER-LATE-CANCEL for a late customer cancellation (BR-045),
 *                                   then the wallet refund of what is left (BR-041); coupon released
 *                                   (consumed for a late customer cancellation, BR-011);
 *   items removed / replaced      → the wallet difference refunded, the coupon amount updated (BR-048).
 * Every entry has its idempotency key, so a replayed event never posts twice (INV-03, INV-09). */
public class LedgerOrderEventHandler : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<PricingPlan, Guid> _planRepository;
    private readonly OrderPostingService _posting;
    private readonly PaymentPostingService _paymentPosting;
    private readonly PaymentManager _paymentManager;
    private readonly CouponManager _couponManager;
    private readonly CashManager _cashManager;

    public LedgerOrderEventHandler(
        IRepository<Order, Guid> orderRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Driver, Guid> driverRepository,
        IRepository<PricingPlan, Guid> planRepository,
        OrderPostingService posting,
        PaymentPostingService paymentPosting,
        PaymentManager paymentManager,
        CouponManager couponManager,
        CashManager cashManager)
    {
        _orderRepository = orderRepository;
        _assignmentRepository = assignmentRepository;
        _driverRepository = driverRepository;
        _planRepository = planRepository;
        _posting = posting;
        _paymentPosting = paymentPosting;
        _paymentManager = paymentManager;
        _couponManager = couponManager;
        _cashManager = cashManager;
    }

    public async Task HandleEventAsync(OrderChangedEto e)
    {
        if (e.EventType == OrderEventType.Modified)
        {
            var modified = await _orderRepository.GetAsync(e.OrderId, includeDetails: false);
            await _couponManager.UpdateAmountAsync(modified.Id, modified.DiscountAmount);
            await _paymentManager.SyncRefundsAsync(modified, RefundReason.Modification);
            return;
        }

        if (e.EventType != OrderEventType.Transition ||
            e.Status is not (OrderStatus.Delivered or OrderStatus.DeliveryFailed or OrderStatus.Cancelled))
        {
            return;
        }

        var order = await _orderRepository.GetAsync(e.OrderId, includeDetails: false);
        switch (order.Status)
        {
            case OrderStatus.Delivered:
                await PostDeliveredAsync(order);
                break;
            case OrderStatus.DeliveryFailed:
                await PostDeliveryFailedAsync(order);
                break;
            case OrderStatus.Cancelled:
                await PostCancelledAsync(order);
                break;
        }
    }

    private async Task PostDeliveredAsync(Order order)
    {
        var assignment = await FindFinishedAssignmentAsync(order.Id);
        if (assignment == null)
        {
            return;
        }

        if (order.PaymentMethod == PaymentMethod.Wallet)
        {
            await _paymentPosting.PostWalletDeliveredAsync(order, notReceived: false);
        }
        else
        {
            await _posting.PostCashDeliveredAsync(order, assignment.DriverId);
            await _posting.PostChangeShortfallAsync(order, assignment.DriverId);
        }

        await _posting.PostDriverPayAsync(order, assignment.DriverId, assignment.DriverPayAmount);
        await _couponManager.ConsumeAsync(order.Id);
        if (order.PaymentMethod == PaymentMethod.Cash)
        {
            await _cashManager.CheckCapAsync(await _driverRepository.GetAsync(assignment.DriverId));
        }
    }

    private async Task PostDeliveryFailedAsync(Order order)
    {
        var assignment = await FindFinishedAssignmentAsync(order.Id);
        switch (order.FailureReason)
        {
            case FailureReason.CustomerNoShow:
                if (order.PaymentMethod == PaymentMethod.Wallet)
                {
                    await _paymentPosting.PostWalletDeliveredAsync(order, notReceived: true);
                }
                else
                {
                    await _posting.PostNotReceivedAsync(order);
                }

                if (assignment != null)
                {
                    await _posting.PostDriverPayAsync(order, assignment.DriverId, assignment.DriverPayAmount);
                }

                await _couponManager.ConsumeAsync(order.Id);
                break;

            case FailureReason.DriverIncident:
                // REC-07: the customer is not charged — a wallet payment is refunded in full.
                await _posting.PostDriverIncidentAsync(order);
                await _paymentManager.SyncRefundsAsync(order, RefundReason.Cancellation);
                await _couponManager.ReleaseAsync(order.Id);
                break;

            // QUALITY_REFUSAL / ADDRESS_NOT_FOUND: who pays is decided case by case through a complaint
            // and an approved adjustment (BR-063 §2), not automatically.
        }
    }

    private async Task PostCancelledAsync(Order order)
    {
        var cause = order.CancelChargedTo ?? CancelCause.Company;
        var prepaid = order.PaymentMethod == PaymentMethod.Wallet ? await _paymentPosting.PrepaidRemainingAsync(order.Id) : 0;

        if (order.CancelReason == CancelReason.CustomerLateCancel)
        {
            await _posting.PostCustomerLateCancelAsync(order, prepaid);
            prepaid = order.PaymentMethod == PaymentMethod.Wallet ? await _paymentPosting.PrepaidRemainingAsync(order.Id) : 0;
        }

        // BR-069: nothing before the driver reaches the restaurant; part of his pay after (before pickup).
        var assignment = (await _assignmentRepository.GetListAsync(a => a.OrderId == order.Id &&
                a.ArrivedAtRestaurantAt != null && a.PickedUpAt == null &&
                (a.Status == AssignmentStatus.Assigned || (a.Status == AssignmentStatus.Released && a.ReleaseReason == "ORDER_CANCELLED"))))
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefault();
        if (assignment != null)
        {
            var plan = await _planRepository.GetAsync(order.PricingPlanId);
            var pay = PricingCalculator.RoundHalfUp(assignment.DriverPayAmount * plan.CancelAfterArrivalPayPct, 100);
            await _posting.PostCancelDriverPayAsync(order, assignment.DriverId, pay, cause, prepaid);
        }

        await _paymentManager.SyncRefundsAsync(order, RefundReason.Cancellation);

        if (order.CancelReason == CancelReason.CustomerLateCancel)
        {
            await _couponManager.ConsumeAsync(order.Id);
        }
        else
        {
            await _couponManager.ReleaseAsync(order.Id);
        }
    }

    private async Task<DeliveryAssignment?> FindFinishedAssignmentAsync(Guid orderId)
    {
        return (await _assignmentRepository.GetListAsync(a => a.OrderId == orderId &&
                (a.Status == AssignmentStatus.Delivered || a.Status == AssignmentStatus.Failed)))
            .OrderByDescending(a => a.AssignedAt).FirstOrDefault();
    }
}
