using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Services;

namespace Saham.Services.Drivers;

public interface IDriverTaskAppService : IApplicationService
{
    Task<DriverTaskDto> ArriveAtRestaurantAsync(Guid assignmentId, DriverActionDto input);

    Task<DriverTaskDto> PickUpAsync(Guid assignmentId, DriverActionDto input);

    Task<DriverTaskDto> ArriveAtCustomerAsync(Guid assignmentId, DriverActionDto input);

    Task<DeliverResultDto> DeliverAsync(Guid assignmentId, DeliverDto input);

    Task<DriverTaskDto> ReportCustomerNotAnsweringAsync(Guid assignmentId);

    Task GiveUpAsync(Guid assignmentId, ReasonDto input);
}
