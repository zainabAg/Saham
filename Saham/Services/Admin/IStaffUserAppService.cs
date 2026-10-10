using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Admin;

public interface IStaffUserAppService : IApplicationService
{
    Task<PagedResultDto<StaffUserDto>> GetListAsync(GetStaffUsersInput input);

    Task<StaffUserDto> CreateAsync(CreateStaffUserDto input);

    Task<StaffUserDto> UpdateRolesAsync(Guid id, UpdateStaffRolesDto input);

    Task SetActiveAsync(Guid id, SetUserActiveDto input);
}
