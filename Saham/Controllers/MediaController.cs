using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saham.Entities.Restaurants;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace Saham.Controllers;

/* Public read-only access to restaurant logos and menu photos (the URLs in LogoUrl / ImageUrl).
 * The bucket stays private; only names in RestaurantMediaManager's pattern are served, so the
 * private driver documents can never be reached through here. A name never changes content,
 * hence the year-long cache: phones and any CDN in front keep the image. */
[AllowAnonymous]
[DisableAuditing]
[Route("media/restaurants")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MediaController : AbpController
{
    private readonly RestaurantMediaManager _mediaManager;

    public MediaController(RestaurantMediaManager mediaManager)
    {
        _mediaManager = mediaManager;
    }

    [HttpGet("{**blobName}")]
    public async Task<IActionResult> GetAsync(string blobName)
    {
        if (!RestaurantMediaManager.IsValidBlobName(blobName))
        {
            return NotFound();
        }

        var stream = await _mediaManager.GetOrNullAsync(blobName);
        if (stream == null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, RestaurantMediaManager.ContentTypeOf(blobName));
    }
}
