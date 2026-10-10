using System.Threading.Tasks;
using Saham.Entities.Drivers;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Saham.Services.Drivers;

public interface IDriverProfileAppService : IApplicationService
{
    Task<DriverProfileDto> RegisterAsync(RegisterDriverDto input);

    Task<DriverProfileDto> GetAsync();

    Task<DriverDocumentDto> UploadDocumentAsync(DriverDocumentType docType, IRemoteStreamContent file);

    Task<DriverProfileDto> SetAvailabilityAsync(SetAvailabilityDto input);
}
