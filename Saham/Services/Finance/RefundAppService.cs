using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Orders;
using Saham.Entities.Payments;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Finance;

public interface IRefundAppService : IApplicationService
{
    Task<PagedResultDto<RefundDto>> GetListAsync(GetRefundsInput input);

    Task<RefundDto> RetryAsync(Guid id);

    Task<RefundDto> ResolveManuallyAsync(Guid id, ResolveRefundDto input);
}

/* Refunds for the accountant (UC-22): automatic ones run by themselves (RefundWorker); the ones in
 * MANUAL_REVIEW are tried again (refunds.retry_or_resolve), or settled outside the system with a
 * reference — cash or bank — by the accountant alone (refunds.execute_real, BR-070). */
[Authorize(SahamPermissions.Refunds.RetryOrResolve)]
public class RefundAppService : SahamAppService, IRefundAppService
{
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly PaymentManager _paymentManager;

    public RefundAppService(IRepository<Refund, Guid> refundRepository, IRepository<Order, Guid> orderRepository, PaymentManager paymentManager)
    {
        _refundRepository = refundRepository;
        _orderRepository = orderRepository;
        _paymentManager = paymentManager;
    }

    public async Task<PagedResultDto<RefundDto>> GetListAsync(GetRefundsInput input)
    {
        var queryable = (await _refundRepository.GetQueryableAsync())
            .WhereIf(input.Status.HasValue, r => r.Status == input.Status)
            .WhereIf(input.OrderId.HasValue, r => r.OrderId == input.OrderId);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var refunds = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(r => r.Status == RefundStatus.ManualReview ? 0 : 1)
            .ThenByDescending(r => r.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<RefundDto>(totalCount, await MapAsync(refunds));
    }

    public async Task<RefundDto> RetryAsync(Guid id)
    {
        var refund = await _refundRepository.GetAsync(id);
        refund.Retry(Clock.Now);

        await _refundRepository.UpdateAsync(refund, autoSave: true);
        return (await MapAsync(new List<Refund> { refund })).Single();
    }

    [Idempotent]
    [Authorize(SahamPermissions.Refunds.ExecuteReal)]
    public async Task<RefundDto> ResolveManuallyAsync(Guid id, ResolveRefundDto input)
    {
        // BR-070 / SOD: real money goes back through the accountant alone. The permission is not
        // enough on its own: ABP's built-in "admin" role (the owner's account) holds every permission.
        if (!CurrentUser.IsInRole(SahamRoles.Accountant))
        {
            throw new AbpAuthorizationException();
        }

        var refund = await _refundRepository.GetAsync(id);
        await _paymentManager.ResolveRefundManuallyAsync(refund, CurrentUser.GetId(), input.Reference, input.Channel);

        await _refundRepository.UpdateAsync(refund, autoSave: true);
        return (await MapAsync(new List<Refund> { refund })).Single();
    }

    private async Task<List<RefundDto>> MapAsync(List<Refund> refunds)
    {
        var ids = refunds.Select(r => r.OrderId).Distinct().ToList();
        var numbers = (await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.Number }))).ToDictionary(o => o.Id, o => o.Number);

        return refunds.Select(r =>
        {
            var dto = ObjectMapper.Map<Refund, RefundDto>(r);
            dto.OrderNumber = numbers.GetValueOrDefault(r.OrderId);
            return dto;
        }).ToList();
    }
}
