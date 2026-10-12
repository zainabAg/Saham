using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Volo.Abp.BlobStoring.Aws;
using Volo.Abp.DependencyInjection;

namespace Saham.Integrations.Storage;

/* ABP's AWS provider only talks to Amazon's own endpoints. This points it at any S3-compatible
 * service: custom endpoint + path-style URLs (bucket in the path, not the host name).
 * Checksums only when required: non-AWS services reject the SDK v4 default trailing checksums. */
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAmazonS3ClientFactory))]
public class S3CompatibleClientFactory : IAmazonS3ClientFactory, ITransientDependency
{
    private readonly S3StorageOptions _options;

    public S3CompatibleClientFactory(IOptions<S3StorageOptions> options)
    {
        _options = options.Value;
    }

    public virtual Task<AmazonS3Client> GetAmazonS3Client(AwsBlobProviderConfiguration configuration)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            AuthenticationRegion = configuration.Region,
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        var credentials = new BasicAWSCredentials(configuration.AccessKeyId, configuration.SecretAccessKey);
        return Task.FromResult(new AmazonS3Client(credentials, config));
    }
}
