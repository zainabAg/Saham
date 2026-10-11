using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Coupons;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Admin;
using Saham.Services.Dtos.Coupons;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Restaurants;

public interface IRestaurantCouponAppService : IApplicationService
{
    Task<PagedResultDto<CouponDto>> GetListAsync(PagedResultRequestDto input);

    Task<CouponDto> RequestAsync(CouponTermsDto input);
}

/* restaurant.coupon_request — the restaurant manager asks for a coupon on their own restaurant,
 * funded from the restaurant's share (K_r, BR-012). It waits for the owner (PENDING_APPROVAL, BR-072),
 * who sees a COUPON_REQUESTED item in the incident queue (I.2-6: RESTAURANT_COUPON_REQUESTED → owner). */
[Authorize(SahamPermissions.Restaurants.CouponRequest)]
public class RestaurantCouponAppService : SahamAppService, IRestaurantCouponAppService
{
    private readonly IRepository<Coupon, Guid> _couponRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly CouponManager _couponManager;
    private readonly IncidentManager _incidentManager;
    private readonly ICurrentScope _currentScope;

    public RestaurantCouponAppService(
        IRepository<Coupon, Guid> couponRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        CouponManager couponManager,
        IncidentManager incidentManager,
        ICurrentScope currentScope)
    {
        _couponRepository = couponRepository;
        _restaurantRepository = restaurantRepository;
        _couponManager = couponManager;
        _incidentManager = incidentManager;
        _currentScope = currentScope;
    }

    public async Task<PagedResultDto<CouponDto>> GetListAsync(PagedResultRequestDto input)
    {
        var restaurantId = await _currentScope.GetRestaurantIdAsync();
        var queryable = (await _couponRepository.GetQueryableAsync()).Where(c => c.RestaurantId == restaurantId);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var coupons = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(c => c.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<CouponDto>(totalCount, await CouponMapper.MapAsync(coupons, ObjectMapper, _restaurantRepository));
    }

    public async Task<CouponDto> RequestAsync(CouponTermsDto input)
    {
        var restaurantId = await _currentScope.GetRestaurantIdAsync();
        var coupon = await _couponManager.RequestByRestaurantAsync(CurrentUser.GetId(), restaurantId, input.Code, input.DiscountType,
            input.Value, input.MaxDiscount, input.MinSubtotal, input.StartsAt, input.EndsAt, input.UsageLimitTotal, input.UsageLimitPerCustomer);
        await _incidentManager.OpenAsync(IncidentType.CouponRequested, IncidentSeverity.Low, coupon.Id, restaurantId: restaurantId);

        return (await CouponMapper.MapAsync(new() { coupon }, ObjectMapper, _restaurantRepository)).Single();
    }
}
