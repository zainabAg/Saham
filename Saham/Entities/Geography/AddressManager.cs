using System;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using Saham.Identity;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Saham.Entities.Geography;

/* Creates and edits saved addresses with the rules of DER-ADR-02:
 * description of at least N characters (ASM-22) and a point inside the city's service area.
 * The distance to the restaurant is checked later, when a quote is calculated. */
public class AddressManager : DomainService
{
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly ISettingProvider _settingProvider;

    public AddressManager(IRepository<City, Guid> cityRepository, ISettingProvider settingProvider)
    {
        _cityRepository = cityRepository;
        _settingProvider = settingProvider;
    }

    public async Task<CustomerAddress> CreateAsync(
        Guid userId,
        Guid cityId,
        AddressLabel label,
        string? customLabel,
        Point location,
        string description,
        string? recipientName,
        string? recipientPhone)
    {
        var address = new CustomerAddress(GuidGenerator.Create(), userId, cityId);
        await SetAsync(address, cityId, label, customLabel, location, description, recipientName, recipientPhone);
        return address;
    }

    public async Task SetAsync(
        CustomerAddress address,
        Guid cityId,
        AddressLabel label,
        string? customLabel,
        Point location,
        string description,
        string? recipientName,
        string? recipientPhone)
    {
        var minLength = await _settingProvider.GetAsync<int>(SahamSettings.Orders.AddressMinDescriptionLength);
        if (description.IsNullOrWhiteSpace() || description.Trim().Length < minLength)
        {
            throw new BusinessException(SahamErrorCodes.AddressDescriptionTooShort)
                .WithData("minLength", minLength);
        }

        var city = await _cityRepository.GetAsync(cityId);
        if (!city.IsActive || !city.Serves(location))
        {
            throw new BusinessException(SahamErrorCodes.AddressOutsideServiceArea);
        }

        var normalizedRecipientPhone = recipientPhone.IsNullOrWhiteSpace()
            ? null
            : PhoneNumberNormalizer.Normalize(recipientPhone);

        address.Set(cityId, label, customLabel, location, description, recipientName, normalizedRecipientPhone);
    }
}
