using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Geography;
using Saham.Entities.Shared;
using Saham.Permissions;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Customers;

/* The customer's saved addresses (REQ-ADR-01..05). A customer only ever sees their own;
 * deleting archives the address (soft delete), past orders keep their own copy. */
[Authorize(SahamPermissions.Orders.Create)]
public class AddressAppService : SahamAppService, IAddressAppService
{
    private readonly IRepository<CustomerAddress, Guid> _addressRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly AddressManager _addressManager;

    public AddressAppService(
        IRepository<CustomerAddress, Guid> addressRepository,
        IRepository<City, Guid> cityRepository,
        AddressManager addressManager)
    {
        _addressRepository = addressRepository;
        _cityRepository = cityRepository;
        _addressManager = addressManager;
    }

    public async Task<ListResultDto<AddressDto>> GetListAsync()
    {
        var userId = CurrentUser.GetId();
        var addresses = await _addressRepository.GetListAsync(x => x.UserId == userId);

        return new ListResultDto<AddressDto>(ObjectMapper.Map<List<CustomerAddress>, List<AddressDto>>(
            addresses.OrderBy(x => x.Label).ThenByDescending(x => x.CreationTime).ToList()));
    }

    public async Task<AddressDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<CustomerAddress, AddressDto>(await GetOwnAsync(id));
    }

    public async Task<AddressDto> CreateAsync(CreateUpdateAddressDto input)
    {
        var address = await _addressManager.CreateAsync(
            CurrentUser.GetId(),
            await ResolveCityIdAsync(input.CityId),
            input.Label,
            input.CustomLabel,
            GeoFactory.CreatePoint(input.Location.Latitude, input.Location.Longitude),
            input.Description,
            input.RecipientName,
            input.RecipientPhone);

        await _addressRepository.InsertAsync(address);
        return ObjectMapper.Map<CustomerAddress, AddressDto>(address);
    }

    public async Task<AddressDto> UpdateAsync(Guid id, CreateUpdateAddressDto input)
    {
        var address = await GetOwnAsync(id);

        await _addressManager.SetAsync(
            address,
            await ResolveCityIdAsync(input.CityId ?? address.CityId),
            input.Label,
            input.CustomLabel,
            GeoFactory.CreatePoint(input.Location.Latitude, input.Location.Longitude),
            input.Description,
            input.RecipientName,
            input.RecipientPhone);

        await _addressRepository.UpdateAsync(address);
        return ObjectMapper.Map<CustomerAddress, AddressDto>(address);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _addressRepository.DeleteAsync(await GetOwnAsync(id));
    }

    /* Another customer's address is reported as not found. */
    private async Task<CustomerAddress> GetOwnAsync(Guid id)
    {
        var address = await _addressRepository.GetAsync(id);
        if (address.UserId != CurrentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(CustomerAddress), id);
        }

        return address;
    }

    private async Task<Guid> ResolveCityIdAsync(Guid? cityId)
    {
        if (cityId.HasValue)
        {
            return cityId.Value;
        }

        var queryable = (await _cityRepository.GetQueryableAsync())
            .Where(c => c.IsActive)
            .OrderBy(c => c.CreationTime)
            .Select(c => c.Id);

        var id = await AsyncExecuter.FirstOrDefaultAsync(queryable);
        return id != Guid.Empty ? id : throw new EntityNotFoundException(typeof(City));
    }
}
