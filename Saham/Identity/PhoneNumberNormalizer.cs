using System.Linq;
using Volo.Abp;

namespace Saham.Identity;

/* Normalizes phone numbers to E.164 (users.phone in SRS 13.4).
 * Iraqi mobiles may be typed as 07XXXXXXXXX, 7XXXXXXXXX, 9647..., 009647... or +9647...;
 * foreign visitors (S-14) must type the full international number. */
public static class PhoneNumberNormalizer
{
    private const string IraqCountryCode = "964";

    public static string Normalize(string? input)
    {
        if (input.IsNullOrWhiteSpace())
        {
            throw Invalid();
        }

        var trimmed = input!.Trim();
        var hasPlus = trimmed.StartsWith("+");
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        if (!hasPlus && digits.StartsWith("00"))
        {
            digits = digits[2..];
            hasPlus = true;
        }

        if (!hasPlus)
        {
            if (digits.Length == 11 && digits.StartsWith("07"))
            {
                digits = IraqCountryCode + digits[1..];
            }
            else if (digits.Length == 10 && digits.StartsWith("7"))
            {
                digits = IraqCountryCode + digits;
            }
            else if (!(digits.Length == 13 && digits.StartsWith(IraqCountryCode + "7")))
            {
                throw Invalid();
            }
        }

        if (digits.StartsWith(IraqCountryCode) && !(digits.Length == 13 && digits[3] == '7'))
        {
            throw Invalid();
        }

        if (digits.Length is < 8 or > 15)
        {
            throw Invalid();
        }

        return "+" + digits;
    }

    private static BusinessException Invalid() => new(SahamErrorCodes.InvalidPhoneNumber);
}
