using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Saham.Entities.Pricing;

/* Everything the customer was shown, frozen at quote time (SRS quotes.payload, BR-014).
 * The order copies prices, names and address from here; the hash lets order creation
 * detect that something changed since the quote (PRICE_CHANGED). */
public record QuoteSnapshot(
    Guid RestaurantId,
    Guid PricingPlanId,
    QuoteAddressSnapshot Address,
    IReadOnlyList<QuoteLineSnapshot> Lines,
    PriceBreakdown Breakdown,
    long DebtCollected,
    long CreditApplied,
    long AmountDue,
    bool CashAllowed,
    DateTime EtaMinAt,
    DateTime EtaMaxAt)
{
    /* Hash of the price-relevant parts only (not the ETA, which moves with time). */
    public string ComputeHash()
    {
        var relevant = new { RestaurantId, PricingPlanId, Address, Lines, Breakdown, DebtCollected, CreditApplied, AmountDue };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(relevant))));
    }
}

public record QuoteLineSnapshot(
    Guid MenuItemId,
    QuoteText Name,
    long UnitPrice,
    int Quantity,
    long LineTotal);

public record QuoteAddressSnapshot(
    Guid AddressId,
    double Latitude,
    double Longitude,
    string Description,
    string Label,
    string? RecipientName,
    string? RecipientPhone);

public record QuoteText(string Ar, string? En, string? Fa, string? Ur);
