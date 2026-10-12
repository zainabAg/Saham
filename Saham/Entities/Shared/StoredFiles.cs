using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BlobStoring;
using Volo.Abp.Uow;

namespace Saham.Entities.Shared;

/* Shared rules for uploaded files (driver documents, restaurant images). */
public static class StoredFiles
{
    /* The declared Content-Type comes from the client: the first bytes must agree with it,
     * so a page or a script renamed to .png is refused. The stream is rewound afterwards. */
    public static bool HasSignature(Stream content, string contentType)
    {
        var head = new byte[12];
        var read = content.Read(head, 0, head.Length);
        content.Position = 0;

        bool StartsWith(params byte[] magic) => read >= magic.Length && head.AsSpan(0, magic.Length).SequenceEqual(magic);

        return contentType switch
        {
            "image/jpeg" => StartsWith(0xFF, 0xD8, 0xFF),
            "image/png" => StartsWith(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            "image/webp" => StartsWith(0x52, 0x49, 0x46, 0x46) && read >= 12 && head.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            "application/pdf" => StartsWith(0x25, 0x50, 0x44, 0x46), // %PDF
            _ => false
        };
    }

    /* A replaced file is deleted only once the change is saved: if the transaction rolls back, the
     * database still points at the old file, so it must still be there. A failed delete only leaves
     * an orphan file behind; it never fails a request that has already been committed. */
    public static async Task DeleteAfterCommitAsync<TContainer>(
        IUnitOfWorkManager unitOfWorkManager,
        IBlobContainer<TContainer> container,
        string blobName,
        ILogger logger)
        where TContainer : class
    {
        async Task DeleteAsync()
        {
            try
            {
                await container.DeleteAsync(blobName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete replaced file {BlobName}", blobName);
            }
        }

        var unitOfWork = unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            await DeleteAsync();
            return;
        }

        unitOfWork.OnCompleted(DeleteAsync);
    }
}
