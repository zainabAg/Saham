using System;
using Saham.Entities.Shared;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Orders;

/* An order line with the name and price frozen at ordering time (BR-014). */
public class OrderItem : Entity<Guid>
{
    public Guid OrderId { get; private set; }

    public Guid MenuItemId { get; private set; }

    public I18nText NameSnapshot { get; private set; }

    public long UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public long LineTotal { get; private set; }

    public OrderItemStatus Status { get; private set; }

    /* Marked by the kitchen as unavailable, waiting for the customer's decision (BR-047). */
    public bool IsReportedUnavailable { get; private set; }

    public string? RemovedReason { get; private set; }

    /* BR-047 (b): a substitute proposed by the kitchen for this unavailable line (ProposedSubstitute
     * until the customer accepts it, then Active). */
    public Guid? SubstituteForItemId { get; private set; }

    public DateTime? RemovedAt { get; private set; }

    protected OrderItem()
    {
        NameSnapshot = null!;
    }

    internal OrderItem(Guid id, Guid orderId, Guid menuItemId, I18nText nameSnapshot, long unitPrice, int quantity)
        : base(id)
    {
        OrderId = orderId;
        MenuItemId = menuItemId;
        NameSnapshot = nameSnapshot;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = unitPrice * quantity;
        Status = OrderItemStatus.Active;
    }

    /* ASM-30: a substitute costs the same or less, keeps the quantity of the line it replaces. */
    internal static OrderItem ProposeSubstitute(Guid id, OrderItem original, Guid menuItemId, I18nText name, long unitPrice)
    {
        return new OrderItem(id, original.OrderId, menuItemId, name, unitPrice, original.Quantity)
        {
            Status = OrderItemStatus.ProposedSubstitute,
            SubstituteForItemId = original.Id
        };
    }

    internal void ActivateSubstitute() => Status = OrderItemStatus.Active;

    internal void ReportUnavailable() => IsReportedUnavailable = true;

    internal void ClearUnavailableReport() => IsReportedUnavailable = false;

    internal void Remove(string reason, DateTime now)
    {
        Status = OrderItemStatus.Removed;
        IsReportedUnavailable = false;
        RemovedReason = reason;
        RemovedAt = now;
    }
}
