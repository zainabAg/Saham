using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Saham.Services.Dtos.Finance;
using Saham.Services.Dtos.Orders;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Ops;

public interface IOpsOrderAppService : IApplicationService
{
    Task<PagedResultDto<OpsOrderDto>> GetListAsync(GetOpsOrdersInput input);

    Task<OpsOrderDto> GetAsync(Guid id);

    Task<OpsOrderDto> CancelAsync(Guid id, CancelOrderDto input);

    Task<OpsOrderDto> DecideItemsAsync(Guid id, ItemDecisionDto input);

    Task<OpsOrderDto> AssignDriverAsync(Guid id, AssignDriverDto input);

    Task<OpsOrderDto> ReleaseDriverAsync(Guid id, ReasonDto input);

    Task<OpsOrderDto> ForceDeliverAsync(Guid id, ReasonDto input);

    Task RecordContactAttemptAsync(Guid id, RecordContactAttemptDto input);

    Task<OpsOrderDto> MarkNotReceivedAsync(Guid id, MarkFailedDto input);

    Task<OpsOrderDto> MarkDriverIncidentAsync(Guid id, ReasonDto input);
}
