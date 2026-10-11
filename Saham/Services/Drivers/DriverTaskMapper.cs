using System;
using System.Threading.Tasks;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Services.Dtos.Drivers;
using Saham.Services.Dtos.Shared;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Drivers;

/* What a driver may see (BR-036, TD-03): never a phone number or the delivery code;
 * the address only once the task is his. */
public class DriverTaskMapper : ITransientDependency
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;

    public DriverTaskMapper(IRepository<Restaurant, Guid> restaurantRepository, IRepository<IdentityUser, Guid> userRepository)
    {
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
    }

    public async Task<DriverOfferDto> ToOfferAsync(DeliveryOffer offer, Order order, DateTime now)
    {
        var restaurant = await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
        return new DriverOfferDto
        {
            Id = offer.Id,
            OrderId = order.Id,
            OrderNumber = order.Number,
            ExpiresAt = offer.ExpiresAt,
            ServerNow = now,
            RestaurantName = restaurant.Name.Localize(),
            RestaurantAddress = restaurant.AddressText,
            RestaurantLocation = new LocationDto { Latitude = restaurant.Location.Y, Longitude = restaurant.Location.X },
            DistanceToRestaurantM = offer.DistanceToRestaurantM,
            DeliveryDistanceM = order.DistanceM,
            DriverPay = order.DriverPayAmount,
            PaymentMethod = order.PaymentMethod,
            AmountToCollect = order.PaymentMethod == PaymentMethod.Cash ? order.AmountDue : 0,
            CashTendered = order.CashTendered,
            ChangeToGive = order.CashTendered.HasValue ? order.CashTendered.Value - order.AmountDue : null
        };
    }

    public async Task<DriverTaskDto> ToTaskAsync(DeliveryAssignment assignment, Order order)
    {
        var restaurant = await _restaurantRepository.GetAsync(order.RestaurantId, includeDetails: false);
        var recipient = order.RecipientName ?? (await _userRepository.FindAsync(order.CustomerId))?.Name;

        return new DriverTaskDto
        {
            AssignmentId = assignment.Id,
            OrderId = order.Id,
            OrderNumber = order.Number,
            OrderStatus = order.Status,
            AssignmentStatus = assignment.Status,
            ArrivedAtRestaurantAt = assignment.ArrivedAtRestaurantAt,
            PickedUpAt = assignment.PickedUpAt,
            ArrivedAtCustomerAt = assignment.ArrivedAtCustomerAt,
            IsBatch = assignment.IsBatch,
            DriverPay = assignment.DriverPayAmount,
            RestaurantName = restaurant.Name.Localize(),
            RestaurantAddress = restaurant.AddressText,
            RestaurantLocation = new LocationDto { Latitude = restaurant.Location.Y, Longitude = restaurant.Location.X },
            EstimatedReadyAt = order.EstimatedReadyAt,
            RecipientFirstName = recipient.IsNullOrWhiteSpace() ? string.Empty : recipient!.Trim().Split(' ')[0],
            DeliveryDescription = order.DeliveryDescription,
            DeliveryLocation = new LocationDto { Latitude = order.DeliveryLocation.Y, Longitude = order.DeliveryLocation.X },
            PaymentMethod = order.PaymentMethod,
            AmountToCollect = order.PaymentMethod == PaymentMethod.Cash ? order.AmountDue : 0,
            ChangeToGive = order.CashTendered.HasValue ? order.CashTendered.Value - order.AmountDue : null
        };
    }
}
