using System.Threading.Tasks;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Admin;

public interface ISmsLogAppService : IApplicationService
{
    Task<PagedResultDto<SmsLogDto>> GetListAsync(GetSmsLogsInput input);
}
