using System;
using Saham.Entities.Shared;

namespace Saham.Entities.Orders;

/* The kitchen's suggested replacement for an unavailable line (BR-047 b), read from its own menu. */
public record ProposedSubstitute(Guid OrderItemId, Guid MenuItemId, I18nText Name, long UnitPrice);
