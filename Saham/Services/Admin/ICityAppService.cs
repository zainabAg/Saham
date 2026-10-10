using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Admin;

public interface ICityAppService : IApplicationService
{
    Task<ListResultDto<CityDto>> GetListAsync();

    Task<CityDto> UpdateServiceAreaAsync(Guid id, UpdateServiceAreaDto input);
}
