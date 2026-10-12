using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Saham.Entities.Shared;
using Saham.Identity;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Saham.Entities.Drivers;

/* Driver registration and document review (UC-14, BR-051, SOD-07). */
public class DriverManager : DomainService
{
    private static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "application/pdf" };

    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DriverDocument, Guid> _documentRepository;
    private readonly IBlobContainer<DriverDocumentContainer> _blobContainer;
    private readonly IdentityUserManager _userManager;
    private readonly ISettingProvider _settingProvider;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DriverManager(
        IRepository<Driver, Guid> driverRepository,
        IRepository<DriverDocument, Guid> documentRepository,
        IBlobContainer<DriverDocumentContainer> blobContainer,
        IdentityUserManager userManager,
        ISettingProvider settingProvider,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _driverRepository = driverRepository;
        _documentRepository = documentRepository;
        _blobContainer = blobContainer;
        _userManager = userManager;
        _settingProvider = settingProvider;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /* A phone user becomes a driver applicant: DRIVER role + a profile waiting for review. */
    public async Task<Driver> RegisterAsync(Guid userId, Guid cityId, string name, string? vehiclePlate, string? emergencyContactPhone)
    {
        if (await _driverRepository.FindAsync(userId) != null)
        {
            throw new BusinessException(SahamErrorCodes.DriverAlreadyRegistered);
        }

        var user = await _userManager.GetByIdAsync(userId);
        user.Name = name.Trim();
        (await _userManager.UpdateAsync(user)).CheckErrors();
        if (!await _userManager.IsInRoleAsync(user, SahamRoles.Driver))
        {
            (await _userManager.AddToRoleAsync(user, SahamRoles.Driver)).CheckErrors();
        }

        var driver = new Driver(
            userId,
            cityId,
            vehiclePlate,
            emergencyContactPhone.IsNullOrWhiteSpace() ? null : PhoneNumberNormalizer.Normalize(emergencyContactPhone),
            await _settingProvider.GetAsync<long>(SahamSettings.Cash.FloatAmount));

        return await _driverRepository.InsertAsync(driver);
    }

    /* Upload or replace one of the three documents. A newer upload replaces a pending one;
     * an accepted document is final. */
    public async Task<DriverDocument> UploadDocumentAsync(
        Driver driver, DriverDocumentType docType, Stream content, string contentType, long sizeBytes)
    {
        if (!AllowedContentTypes.Contains(contentType) || !StoredFiles.HasSignature(content, contentType))
        {
            throw new BusinessException(SahamErrorCodes.DocumentInvalidType);
        }

        if (sizeBytes <= 0 || sizeBytes > DriverDocument.MaxSizeBytes)
        {
            throw new BusinessException(SahamErrorCodes.DocumentTooLarge).WithData("maxMb", DriverDocument.MaxSizeBytes / 1024 / 1024);
        }

        var current = await _documentRepository.FindAsync(d =>
            d.DriverId == driver.Id && d.DocType == docType && d.Status != DriverDocumentStatus.Rejected);
        if (current != null)
        {
            if (current.Status == DriverDocumentStatus.Accepted)
            {
                throw new BusinessException(SahamErrorCodes.DocumentAlreadyAccepted);
            }

            current.Reject("REPLACED", driver.Id, Clock.Now);
            await _documentRepository.UpdateAsync(current, autoSave: true); // frees the "one live document per type" index
        }

        var id = GuidGenerator.Create();
        var blobName = $"{driver.Id:N}/{docType}/{id:N}";
        await _blobContainer.SaveAsync(blobName, content, overrideExisting: false);

        if (current != null)
        {
            // A replaced file is never reviewed: don't keep paying for it.
            await StoredFiles.DeleteAfterCommitAsync(_unitOfWorkManager, _blobContainer, current.BlobName, Logger);
        }

        driver.MarkDocsPending();
        return await _documentRepository.InsertAsync(new DriverDocument(id, driver.Id, docType, blobName, contentType, sizeBytes));
    }

    /* A replaced document's file is deleted (UploadDocumentAsync): say so instead of failing. */
    public async Task<byte[]> GetDocumentContentAsync(DriverDocument document)
    {
        return await _blobContainer.GetAllBytesOrNullAsync(document.BlobName)
               ?? throw new BusinessException(SahamErrorCodes.DocumentFileRemoved);
    }

    /* Ops accept or reject each document (driver.documents.review). When all three are accepted,
     * the driver waits for the owner's final approval (SOD-07). */
    public async Task ReviewDocumentAsync(Driver driver, DriverDocument document, bool accept, string? note, Guid reviewerId)
    {
        if (document.DriverId != driver.Id || document.Status != DriverDocumentStatus.Submitted)
        {
            throw new BusinessException(SahamErrorCodes.InvalidDriverReviewState);
        }

        var now = Clock.Now;
        if (accept)
        {
            document.Accept(reviewerId, now);
        }
        else
        {
            document.Reject(note ?? string.Empty, reviewerId, now);
        }

        await _documentRepository.UpdateAsync(document, autoSave: true);

        var accepted = await _documentRepository.GetListAsync(d => d.DriverId == driver.Id && d.Status == DriverDocumentStatus.Accepted);
        var allTypes = Enum.GetValues<DriverDocumentType>();
        if (allTypes.All(t => accepted.Any(d => d.DocType == t)))
        {
            driver.MarkDocsAccepted(reviewerId, now);
        }
        else
        {
            driver.MarkDocsPending();
        }
    }
}
