using Volo.Abp.BlobStoring;

namespace Saham.Entities.Restaurants;

/* Public images of a restaurant: its logo and its menu photos. Kept apart from the private
 * driver documents (SRS 14.2); the bucket itself stays private, MediaController serves the files. */
[BlobContainerName("restaurant-media")]
public class RestaurantMediaContainer
{
}
