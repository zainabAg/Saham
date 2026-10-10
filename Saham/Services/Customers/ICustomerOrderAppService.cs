using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Orders;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Customers;

public interface ICustomerOrderAppService : IApplicationService
{
    Task<CustomerOrderDto> CreateAsync(CreateOrderDto input);

    Task<CustomerOrderDto> GetAsync(Guid id);

    Task<PagedResultDto<CustomerOrderDto>> GetListAsync(GetCustomerOrdersInput input);

    Task<CustomerOrderDto> CancelAsync(Guid id);

    Task<CustomerOrderDto> DecideItemsAsync(Guid id, ItemDecisionDto input);
}
