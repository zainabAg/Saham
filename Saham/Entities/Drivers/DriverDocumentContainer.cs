using Volo.Abp.BlobStoring;

namespace Saham.Entities.Drivers;

/* Private container for driver documents (SRS 14.2: sensitive files apart from public menu images). */
[BlobContainerName("driver-documents")]
public class DriverDocumentContainer
{
}
