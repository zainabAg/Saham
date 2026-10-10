using Volo.Abp.Application.Services;
using Saham.Localization;

namespace Saham.Services;

/* Inherit your application services from this class. */
public abstract class SahamAppService : ApplicationService
{
    protected SahamAppService()
    {
        LocalizationResource = typeof(SahamResource);
    }
}