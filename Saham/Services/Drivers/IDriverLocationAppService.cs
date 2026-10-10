using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Services;

namespace Saham.Services.Drivers;

public interface IDriverLocationAppService : IApplicationService
{
    Task<PostLocationsResultDto> PostAsync(PostLocationsDto input);
}
