using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Drivers;
using Saham.Entities.Geography;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Saham.Services.Drivers;

/* The driver's own profile (UC-14): register, upload the three documents, go available.
 * A driver signs in with phone + OTP like everyone else, then registers here. */
[Authorize]
public class DriverProfileAppService : SahamAppService, IDriverProfileAppService
{
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DriverDocument, Guid> _documentRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly DriverManager _driverManager;
    private readonly ICurrentScope _currentScope;
    private readonly IdentityUserManager _userManager;

    public DriverProfileAppService(
        IRepository<Driver, Guid> driverRepository,
        IRepository<DriverDocument, Guid> documentRepository,
        IRepository<City, Guid> cityRepository,
        DriverManager driverManager,
        ICurrentScope currentScope,
        IdentityUserManager userManager)
    {
        _driverRepository = driverRepository;
        _documentRepository = documentRepository;
        _cityRepository = cityRepository;
        _driverManager = driverManager;
        _currentScope = currentScope;
        _userManager = userManager;
    }

    public async Task<DriverProfileDto> RegisterAsync(RegisterDriverDto input)
    {
        var cityId = input.CityId ?? (await _cityRepository.FirstOrDefaultAsync(c => c.IsActive))?.Id
                     ?? throw new EntityNotFoundException(typeof(City));

        var driver = await _driverManager.RegisterAsync(CurrentUser.GetId(), cityId, input.Name, input.VehiclePlate, input.EmergencyContactPhone);
        return await MapAsync(driver);
    }

    public async Task<DriverProfileDto> GetAsync()
    {
        return await MapAsync(await _currentScope.GetDriverAsync());
    }

    /* REQ-DRV-01: jpg / png / pdf up to 5 MB, multipart/form-data. */
    [Authorize(SahamPermissions.Drivers.DocumentsUpload)]
    public async Task<DriverDocumentDto> UploadDocumentAsync(DriverDocumentType docType, IRemoteStreamContent file)
    {
        var driver = await _currentScope.GetDriverAsync();

        using var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            await stream.CopyToAsync(buffer);
        }

        buffer.Position = 0;
        var document = await _driverManager.UploadDocumentAsync(driver, docType, buffer, file.ContentType, buffer.Length);
        await _driverRepository.UpdateAsync(driver);

        return ObjectMapper.Map<DriverDocument, DriverDocumentDto>(document);
    }

    /* Only an approved driver can become available; never offline with an order in hand (BR-057). */
    [Authorize(SahamPermissions.Dispatch.OfferRespond)]
    public async Task<DriverProfileDto> SetAvailabilityAsync(SetAvailabilityDto input)
    {
        var driver = await _currentScope.GetDriverAsync();
        driver.SetAvailability(input.IsAvailable);

        await _driverRepository.UpdateAsync(driver);
        return await MapAsync(driver);
    }

    private async Task<DriverProfileDto> MapAsync(Driver driver)
    {
        var user = await _userManager.GetByIdAsync(driver.Id);
        var documents = await _documentRepository.GetListAsync(d => d.DriverId == driver.Id);

        var dto = ObjectMapper.Map<Driver, DriverProfileDto>(driver);
        dto.Name = user.Name;
        dto.PhoneNumber = user.PhoneNumber ?? string.Empty;
        dto.Documents = LatestPerType(documents);
        return dto;
    }

    private List<DriverDocumentDto> LatestPerType(List<DriverDocument> documents) =>
        ObjectMapper.Map<List<DriverDocument>, List<DriverDocumentDto>>(documents
            .GroupBy(d => d.DocType)
            .Select(g => g.OrderByDescending(d => d.CreationTime).First())
            .OrderBy(d => d.DocType)
            .ToList());
}
