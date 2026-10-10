using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Drivers;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Ops;

/* Driver review (UC-14, BR-051):
 *   ops (and owner) look at and accept / reject each document — driver.documents.review;
 *   only the owner approves, rejects or suspends the driver — driver.approve_reject_suspend (SOD-07).
 * Opening a document is a POST so it is always written to the audit log (SRS 5.5). */
[Authorize(SahamPermissions.Drivers.DocumentsReview)]
public class DriverReviewAppService : SahamAppService, IDriverReviewAppService
{
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DriverDocument, Guid> _documentRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly DriverManager _driverManager;

    public DriverReviewAppService(
        IRepository<Driver, Guid> driverRepository,
        IRepository<DriverDocument, Guid> documentRepository,
        IRepository<IdentityUser, Guid> userRepository,
        DriverManager driverManager)
    {
        _driverRepository = driverRepository;
        _documentRepository = documentRepository;
        _userRepository = userRepository;
        _driverManager = driverManager;
    }

    public async Task<PagedResultDto<DriverAdminDto>> GetListAsync(GetDriversInput input)
    {
        var users = await _userRepository.GetQueryableAsync();
        var queryable = (await _driverRepository.GetQueryableAsync())
            .WhereIf(input.ReviewStatus.HasValue, d => d.ReviewStatus == input.ReviewStatus)
            .WhereIf(input.Availability.HasValue, d => d.Availability == input.Availability)
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(), d => users.Any(u => u.Id == d.Id &&
                (u.PhoneNumber!.Contains(input.Filter!) || u.Name!.Contains(input.Filter!))));

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var drivers = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(d => d.ReviewStatus)
            .ThenByDescending(d => d.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<DriverAdminDto>(totalCount, await MapAsync(drivers, includeDocuments: false));
    }

    public async Task<DriverAdminDto> GetAsync(Guid id)
    {
        return (await MapAsync(new List<Driver> { await _driverRepository.GetAsync(id) }, includeDocuments: true)).Single();
    }

    public async Task<IRemoteStreamContent> ViewDocumentAsync(Guid documentId)
    {
        var document = await _documentRepository.GetAsync(documentId);
        var bytes = await _driverManager.GetDocumentContentAsync(document);
        return new RemoteStreamContent(new MemoryStream(bytes), $"{document.DocType}{Extension(document.ContentType)}", document.ContentType);
    }

    public async Task<DriverAdminDto> ReviewDocumentAsync(Guid documentId, ReviewDocumentDto input)
    {
        var document = await _documentRepository.GetAsync(documentId);
        var driver = await _driverRepository.GetAsync(document.DriverId);

        await _driverManager.ReviewDocumentAsync(driver, document, input.Accept, input.Note, CurrentUser.GetId());
        await _driverRepository.UpdateAsync(driver);

        return await GetAsync(driver.Id);
    }

    [Authorize(SahamPermissions.Drivers.ApproveRejectSuspend)]
    public async Task<DriverAdminDto> ApproveAsync(Guid id)
    {
        var driver = await _driverRepository.GetAsync(id);
        driver.Approve(CurrentUser.GetId(), Clock.Now);

        await _driverRepository.UpdateAsync(driver);
        return await GetAsync(id);
    }

    [Authorize(SahamPermissions.Drivers.ApproveRejectSuspend)]
    public async Task<DriverAdminDto> RejectAsync(Guid id, ReasonDto input)
    {
        var driver = await _driverRepository.GetAsync(id);
        driver.Reject(input.Reason, Clock.Now);

        await _driverRepository.UpdateAsync(driver);
        return await GetAsync(id);
    }

    /* Suspended drivers get no new offers; orders already in hand continue. */
    [Authorize(SahamPermissions.Drivers.ApproveRejectSuspend)]
    public async Task<DriverAdminDto> SuspendAsync(Guid id, ReasonDto input)
    {
        var driver = await _driverRepository.GetAsync(id);
        driver.Suspend(input.Reason, Clock.Now);

        await _driverRepository.UpdateAsync(driver);
        return await GetAsync(id);
    }

    private async Task<List<DriverAdminDto>> MapAsync(List<Driver> drivers, bool includeDocuments)
    {
        var ids = drivers.Select(d => d.Id).ToList();
        var users = (await _userRepository.GetListAsync(u => ids.Contains(u.Id))).ToDictionary(u => u.Id);
        var documents = includeDocuments
            ? (await _documentRepository.GetListAsync(d => ids.Contains(d.DriverId))).ToLookup(d => d.DriverId)
            : null;

        return drivers.Select(d =>
        {
            var dto = ObjectMapper.Map<Driver, DriverAdminDto>(d);
            dto.Name = users.GetValueOrDefault(d.Id)?.Name;
            dto.PhoneNumber = users.GetValueOrDefault(d.Id)?.PhoneNumber ?? string.Empty;
            dto.LastLocation = d.LastLocation == null ? null : new LocationDto { Latitude = d.LastLocation.Y, Longitude = d.LastLocation.X };
            dto.Documents = documents == null
                ? new List<DriverDocumentDto>()
                : ObjectMapper.Map<List<DriverDocument>, List<DriverDocumentDto>>(documents[d.Id].OrderBy(x => x.DocType).ThenByDescending(x => x.CreationTime).ToList());
            return dto;
        }).ToList();
    }

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "application/pdf" => ".pdf",
        _ => string.Empty
    };
}
