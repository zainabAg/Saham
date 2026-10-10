using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Drivers;

/* An identity / licence / bike document (SRS table driver_documents). The file lives in the
 * private "driver-documents" blob container; only ops and the owner can open it (audited). */
public class DriverDocument : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    public Guid DriverId { get; private set; }

    public DriverDocumentType DocType { get; private set; }

    public string BlobName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public DriverDocumentStatus Status { get; private set; }

    public string? ReviewNote { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    protected DriverDocument()
    {
        BlobName = null!;
        ContentType = null!;
    }

    internal DriverDocument(Guid id, Guid driverId, DriverDocumentType docType, string blobName, string contentType, long sizeBytes)
        : base(id)
    {
        DriverId = driverId;
        DocType = docType;
        BlobName = blobName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = DriverDocumentStatus.Submitted;
    }

    internal void Accept(Guid reviewedBy, DateTime now)
    {
        Status = DriverDocumentStatus.Accepted;
        ReviewNote = null;
        ReviewedBy = reviewedBy;
        ReviewedAt = now;
    }

    /* A rejection always says why, so the driver can upload a better one (UC-14 A1). */
    internal void Reject(string note, Guid reviewedBy, DateTime now)
    {
        if (note.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.ReasonRequired);
        }

        Status = DriverDocumentStatus.Rejected;
        ReviewNote = note.Trim();
        ReviewedBy = reviewedBy;
        ReviewedAt = now;
    }
}
