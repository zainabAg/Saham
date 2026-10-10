using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Pricing;
using Saham.Permissions;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Customers;

/* Step 1 of ordering (BR-001): the server prices the cart and returns a quote valid for a
 * few minutes. Step 2 (phase 4) creates the order from the quote id. */
[Authorize(SahamPermissions.Orders.Create)]
public class QuoteAppService : SahamAppService, IQuoteAppService
{
    private readonly IRepository<Quote, Guid> _quoteRepository;
    private readonly QuoteManager _quoteManager;

    public QuoteAppService(IRepository<Quote, Guid> quoteRepository, QuoteManager quoteManager)
    {
        _quoteRepository = quoteRepository;
        _quoteManager = quoteManager;
    }

    public async Task<QuoteDto> CreateAsync(CreateQuoteDto input)
    {
        var quote = await _quoteManager.CreateAsync(
            CurrentUser.GetId(),
            input.RestaurantId,
            input.AddressId,
            input.Items.Select(i => (i.MenuItemId, i.Quantity)).ToList(),
            input.OrderType,
            input.ScheduledFor);

        await _quoteRepository.InsertAsync(quote);
        return Map(quote);
    }

    public async Task<QuoteDto> GetAsync(Guid id)
    {
        var quote = await _quoteRepository.GetAsync(id);
        if (quote.CustomerId != CurrentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(Quote), id);
        }

        return Map(quote);
    }

    private static QuoteDto Map(Quote quote)
    {
        var s = quote.Snapshot;
        var b = s.Breakdown;

        return new QuoteDto
        {
            Id = quote.Id,
            ExpiresAt = quote.ExpiresAt,
            RestaurantId = quote.RestaurantId,
            AddressId = quote.AddressId,
            OrderType = quote.OrderType,
            ScheduledFor = quote.ScheduledFor,
            Items = s.Lines.Select(l => new QuoteLineDto
            {
                MenuItemId = l.MenuItemId,
                Name = new Entities.Shared.I18nText(l.Name.Ar, l.Name.En, l.Name.Fa, l.Name.Ur).Localize(),
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                LineTotal = l.LineTotal
            }).ToList(),
            DistanceMeters = b.DistanceM,
            ItemsSubtotal = b.ItemsSubtotal,
            DiscountAmount = b.DiscountAmount,
            DeliveryFeeBase = b.DeliveryFeeBase,
            FreeDeliveryApplied = b.FreeDeliveryApplied,
            DeliveryFee = b.DeliveryFee,
            FreeDeliveryRemaining = b.FreeDeliveryRemaining,
            TotalAmount = b.TotalAmount,
            DebtCollected = s.DebtCollected,
            CreditApplied = s.CreditApplied,
            AmountDue = s.AmountDue,
            CashAllowed = s.CashAllowed,
            EtaMinAt = s.EtaMinAt,
            EtaMaxAt = s.EtaMaxAt
        };
    }
}
