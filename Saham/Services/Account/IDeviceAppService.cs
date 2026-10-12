using System.Threading.Tasks;
using Saham.Services.Dtos.Account;
using Volo.Abp.Application.Services;

namespace Saham.Services.Account;

public interface IDeviceAppService : IApplicationService
{
    Task RegisterAsync(RegisterDeviceDto input);

    Task UnregisterAsync(UnregisterDeviceDto input);
}
