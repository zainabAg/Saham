using Microsoft.Extensions.Localization;
using Saham.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Saham;

[Dependency(ReplaceServices = true)]
public class SahamBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<SahamResource> _localizer;

    public SahamBrandingProvider(IStringLocalizer<SahamResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}