namespace Saham.Integrations.Storage;

/* Configuration section "BlobStorage:S3": any S3-compatible object storage (Neon, R2, AWS, MinIO).
 * Keys belong in appsettings.secrets.json. Empty ServiceUrl or keys = stay on the database provider. */
public class S3StorageOptions
{
    public string? ServiceUrl { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string Region { get; set; } = "us-east-1";

    /* Private bucket for driver documents (SRS 14.2). */
    public string DriverDocumentsBucket { get; set; } = "driver-documents";

    /* Logos and menu photos; private too, served by MediaController. */
    public string RestaurantMediaBucket { get; set; } = "restaurant-media";

    public bool CreateBucketIfNotExists { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl) &&
        !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey);
}
