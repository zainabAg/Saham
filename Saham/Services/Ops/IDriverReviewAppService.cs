using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Saham.Services.Ops;

public interface IDriverReviewAppService : IApplicationService
{
    Task<PagedResultDto<DriverAdminDto>> GetListAsync(GetDriversInput input);

    Task<DriverAdminDto> GetAsync(Guid id);

    Task<IRemoteStreamContent> ViewDocumentAsync(Guid documentId);

    Task<DriverAdminDto> ReviewDocumentAsync(Guid documentId, ReviewDocumentDto input);

    Task<DriverAdminDto> ApproveAsync(Guid id);

    Task<DriverAdminDto> RejectAsync(Guid id, ReasonDto input);

    Task<DriverAdminDto> SuspendAsync(Guid id, ReasonDto input);
}
