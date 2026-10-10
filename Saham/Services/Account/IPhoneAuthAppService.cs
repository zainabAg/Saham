using System.Threading.Tasks;
using Saham.Services.Dtos.Account;
using Volo.Abp.Application.Services;

namespace Saham.Services.Account;

public interface IPhoneAuthAppService : IApplicationService
{
    Task<SendOtpResultDto> SendOtpAsync(SendOtpInput input);
}
