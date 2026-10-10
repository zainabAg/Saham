using System.Collections.Generic;
using System.Globalization;
using Volo.Abp;
using Volo.Abp.Domain.Values;

namespace Saham.Entities.Shared;

/* Translatable text stored as jsonb (SRS *_i18n columns, TD-05):
 * Arabic is required, the other languages are optional and fall back to Arabic. */
public class I18nText : ValueObject
{
    public const int MaxLength = 256;

    public string Ar { get; private set; }

    public string? En { get; private set; }

    public string? Fa { get; private set; }

    public string? Ur { get; private set; }

    protected I18nText()
    {
        Ar = string.Empty;
    }

    public I18nText(string ar, string? en = null, string? fa = null, string? ur = null)
    {
        Ar = Check.NotNullOrWhiteSpace(ar, nameof(ar), MaxLength).Trim();
        En = Normalize(en);
        Fa = Normalize(fa);
        Ur = Normalize(ur);
    }

    /* Text in the current UI culture, falling back to Arabic. */
    public string Localize(CultureInfo? culture = null)
    {
        var language = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        return language switch
        {
            "en" => En ?? Ar,
            "fa" => Fa ?? Ar,
            "ur" => Ur ?? Ar,
            _ => Ar
        };
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Ar;
        yield return En;
        yield return Fa;
        yield return Ur;
    }

    private static string? Normalize(string? value)
    {
        return value.IsNullOrWhiteSpace() ? null : Check.Length(value!.Trim(), nameof(value), MaxLength);
    }
}
