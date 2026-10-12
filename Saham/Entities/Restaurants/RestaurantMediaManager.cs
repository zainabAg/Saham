using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saham.Entities.Shared;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Services;
using Volo.Abp.Uow;

namespace Saham.Entities.Restaurants;

/* Stores restaurant logos and menu photos and turns them into the URL kept on the entity
 * (Restaurant.LogoUrl, MenuItem.ImageUrl). Our own files are recognised by UrlPrefix; any other
 * URL (an image hosted elsewhere) is left alone. Every upload gets a new name, so a URL never
 * changes content and can be cached forever. */
public class RestaurantMediaManager : DomainService
{
    public const string UrlPrefix = "/media/restaurants/";
    public const long MaxSizeBytes = 2 * 1024 * 1024;
    public const string LogoFolder = "logo";
    public const string MenuFolder = "menu";

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    /* "<restaurant>/<logo|menu>/<file>.<ext>": also the only names MediaController will serve. */
    private static readonly Regex BlobNamePattern = new(
        "^[0-9a-f]{32}/(logo|menu)/[0-9a-f]{32}\\.(jpg|png|webp)$", RegexOptions.Compiled);

    private readonly IBlobContainer<RestaurantMediaContainer> _container;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public RestaurantMediaManager(IBlobContainer<RestaurantMediaContainer> container, IUnitOfWorkManager unitOfWorkManager)
    {
        _container = container;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<string> SaveAsync(Guid restaurantId, string folder, Stream content, string contentType, long sizeBytes)
    {
        if (!Extensions.TryGetValue(contentType, out var extension) || !StoredFiles.HasSignature(content, contentType))
        {
            throw new BusinessException(SahamErrorCodes.ImageInvalidType);
        }

        if (sizeBytes <= 0 || sizeBytes > MaxSizeBytes)
        {
            throw new BusinessException(SahamErrorCodes.DocumentTooLarge).WithData("maxMb", MaxSizeBytes / 1024 / 1024);
        }

        var blobName = $"{restaurantId:N}/{folder}/{GuidGenerator.Create():N}{extension}";
        await _container.SaveAsync(blobName, content, overrideExisting: false);
        return UrlPrefix + blobName;
    }

    /* Deletes the file behind a URL (once the change is saved) only when it is ours and belongs to
     * this restaurant, so pointing an item at another restaurant's image can never delete that image. */
    public async Task DeleteIfOwnedAsync(Guid restaurantId, string? url)
    {
        if (url == null || !url.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var blobName = url.Substring(UrlPrefix.Length);
        if (IsValidBlobName(blobName) && blobName.StartsWith($"{restaurantId:N}/", StringComparison.Ordinal))
        {
            await StoredFiles.DeleteAfterCommitAsync(_unitOfWorkManager, _container, blobName, Logger);
        }
    }

    public Task<Stream?> GetOrNullAsync(string blobName) => _container.GetOrNullAsync(blobName);

    public static bool IsValidBlobName(string blobName) => BlobNamePattern.IsMatch(blobName);

    public static string ContentTypeOf(string blobName) =>
        Extensions.First(x => blobName.EndsWith(x.Value, StringComparison.Ordinal)).Key;
}
