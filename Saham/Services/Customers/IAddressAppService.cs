using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Customers;

public interface IAddressAppService : IApplicationService
{
    Task<ListResultDto<AddressDto>> GetListAsync();

    Task<AddressDto> GetAsync(Guid id);

    Task<AddressDto> CreateAsync(CreateUpdateAddressDto input);

    Task<AddressDto> UpdateAsync(Guid id, CreateUpdateAddressDto input);

    Task DeleteAsync(Guid id);
}
