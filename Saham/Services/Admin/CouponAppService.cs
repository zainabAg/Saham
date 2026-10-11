using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Coupons;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Saham.Permissions;
using Saham.Services.Dtos.Coupons;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Admin;

public interface ICouponAppService : IApplicationService
{
    Task<PagedResultDto<CouponDto>> GetListAsync(GetCouponsInput input);

    Task<CouponDto> GetAsync(Guid id);

    Task<CouponDto> CreateAsync(CreateCouponDto input);

    Task<CouponDto> UpdateAsync(Guid id, UpdateCouponDto input);

    Task<CouponDto> ApproveAsync(Guid id);

    Task<CouponDto> RejectAsync(Guid id, RejectCouponDto input);

    Task<CouponDto> DisableAsync(Guid id);

    Task<CouponDto> EnableAsync(Guid id);
}

/* coupons.manage — the owner alone (BR-072, DEC-04/11): creates company or restaurant coupons
 * (active at once), decides on the restaurants' requests, edits terms while nobody used the coupon,
 * and switches coupons off. Every change is in the audit log (BR-055). */
[Authorize(SahamPermissions.Coupons.Manage)]
public class CouponAppService : SahamAppService, ICouponAppService
{
    private readonly IRepository<Coupon, Guid> _couponRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly CouponManager _couponManager;
    private readonly IncidentManager _incidentManager;

    public CouponAppService(
        IRepository<Coupon, Guid> couponRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        CouponManager couponManager,
        IncidentManager incidentManager)
    {
        _couponRepository = couponRepository;
        _restaurantRepository = restaurantRepository;
        _couponManager = couponManager;
        _incidentManager = incidentManager;
    }

    public async Task<PagedResultDto<CouponDto>> GetListAsync(GetCouponsInput input)
    {
        var code = input.Code.IsNullOrWhiteSpace() ? null : input.Code!.Trim().ToUpperInvariant();
        var queryable = (await _couponRepository.GetQueryableAsync())
            .WhereIf(input.Status.HasValue, c => c.Status == input.Status)
            .WhereIf(input.RestaurantId.HasValue, c => c.RestaurantId == input.RestaurantId)
            .WhereIf(code != null, c => c.Code.Contains(code!));

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var coupons = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(c => c.Status)
            .ThenByDescending(c => c.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<CouponDto>(totalCount, await CouponMapper.MapAsync(coupons, ObjectMapper, _restaurantRepository));
    }

    public async Task<CouponDto> GetAsync(Guid id)
    {
        return (await CouponMapper.MapAsync(new List<Coupon> { await _couponRepository.GetAsync(id) }, ObjectMapper, _restaurantRepository)).Single();
    }

    public async Task<CouponDto> CreateAsync(CreateCouponDto input)
    {
        if (input.RestaurantId.HasValue)
        {
            await _restaurantRepository.GetAsync(input.RestaurantId.Value, includeDetails: false);
        }

        var coupon = await _couponManager.CreateByOwnerAsync(CurrentUser.GetId(), input.Code, input.DiscountType, input.Value,
            input.MaxDiscount, input.MinSubtotal, input.StartsAt, input.EndsAt, input.UsageLimitTotal, input.UsageLimitPerCustomer,
            input.FundedBy, input.StackableWithFreeDelivery, input.RestaurantId);
        return await GetAsync(coupon.Id);
    }

    public async Task<CouponDto> UpdateAsync(Guid id, UpdateCouponDto input)
    {
        var coupon = await _couponRepository.GetAsync(id);
        coupon.SetTerms(input.DiscountType, input.Value, input.MaxDiscount, input.MinSubtotal, input.StartsAt, input.EndsAt,
            input.UsageLimitTotal, input.UsageLimitPerCustomer, input.StackableWithFreeDelivery);

        await _couponRepository.UpdateAsync(coupon, autoSave: true);
        return await GetAsync(id);
    }

    public async Task<CouponDto> ApproveAsync(Guid id)
    {
        var coupon = await _couponRepository.GetAsync(id);
        coupon.Approve(CurrentUser.GetId());

        await _couponRepository.UpdateAsync(coupon, autoSave: true);
        await _incidentManager.AutoResolveAsync(IncidentType.CouponRequested, coupon.Id, "APPROVED");
        return await GetAsync(id);
    }

    public async Task<CouponDto> RejectAsync(Guid id, RejectCouponDto input)
    {
        var coupon = await _couponRepository.GetAsync(id);
        coupon.Reject(CurrentUser.GetId(), input.Reason);

        await _couponRepository.UpdateAsync(coupon, autoSave: true);
        await _incidentManager.AutoResolveAsync(IncidentType.CouponRequested, coupon.Id, "REJECTED");
        return await GetAsync(id);
    }

    public async Task<CouponDto> DisableAsync(Guid id)
    {
        var coupon = await _couponRepository.GetAsync(id);
        coupon.Disable();

        await _couponRepository.UpdateAsync(coupon, autoSave: true);
        return await GetAsync(id);
    }

    public async Task<CouponDto> EnableAsync(Guid id)
    {
        var coupon = await _couponRepository.GetAsync(id);
        coupon.Enable(CurrentUser.GetId());

        await _couponRepository.UpdateAsync(coupon, autoSave: true);
        return await GetAsync(id);
    }
}

internal static class CouponMapper
{
    public static async Task<List<CouponDto>> MapAsync(List<Coupon> coupons, Volo.Abp.ObjectMapping.IObjectMapper mapper,
        IRepository<Restaurant, Guid> restaurantRepository)
    {
        var ids = coupons.Where(c => c.RestaurantId.HasValue).Select(c => c.RestaurantId!.Value).Distinct().ToList();
        var names = (await restaurantRepository.GetListAsync(r => ids.Contains(r.Id), includeDetails: false)).ToDictionary(r => r.Id, r => r.Name.Ar);
        return coupons.Select(c =>
        {
            var dto = mapper.Map<Coupon, CouponDto>(c);
            dto.RestaurantName = c.RestaurantId.HasValue ? names.GetValueOrDefault(c.RestaurantId.Value) : null;
            return dto;
        }).ToList();
    }
}
