using System.Threading.Tasks;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Finance;

public interface ICashAppService : IApplicationService
{
    Task<CashMovementDto> RecordHandoverAsync(RecordHandoverDto input);

    Task<CashMovementDto> IssueFloatAsync(MoveFloatDto input);

    Task<CashMovementDto> ReturnFloatAsync(MoveFloatDto input);

    Task<PagedResultDto<CashMovementDto>> GetMovementsAsync(GetCashMovementsInput input);
}
