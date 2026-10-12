using System.IO;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;
using Saham.Localization;

namespace Saham.Services;

/* Inherit your application services from this class. */
public abstract class SahamAppService : ApplicationService
{
    protected SahamAppService()
    {
        LocalizationResource = typeof(SahamResource);
    }

    /* An uploaded file (multipart/form-data) copied to memory, so its real size is known before storing.
     * Stops reading one byte past maxBytes: an oversized upload is refused without being held in memory. */
    protected static async Task<MemoryStream> ReadToMemoryAsync(IRemoteStreamContent file, long maxBytes)
    {
        var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes)
                {
                    await buffer.DisposeAsync();
                    throw new BusinessException(SahamErrorCodes.DocumentTooLarge).WithData("maxMb", maxBytes / 1024 / 1024);
                }
            }
        }

        buffer.Position = 0;
        return buffer;
    }
}