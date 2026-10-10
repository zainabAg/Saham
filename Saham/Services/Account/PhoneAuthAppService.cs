using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Identity;
using Saham.Identity;
using Saham.Services.Dtos.Account;
using Saham.Settings;
using Volo.Abp.Settings;

namespace Saham.Services.Account;

/* Step 1 of phone login: sends the code. Step 2 is the token endpoint
 * with grant_type=phone_otp (see PhoneOtpTokenExtensionGrant). */
[AllowAnonymous]
public class PhoneAuthAppService : SahamAppService, IPhoneAuthAppService
{
    private readonly OtpManager _otpManager;

    public PhoneAuthAppService(OtpManager otpManager)
    {
        _otpManager = otpManager;
    }

    public async Task<SendOtpResultDto> SendOtpAsync(SendOtpInput input)
    {
        var phoneNumber = PhoneNumberNormalizer.Normalize(input.PhoneNumber);
        var challenge = await _otpManager.SendAsync(phoneNumber, OtpPurpose.Login);

        return new SendOtpResultDto
        {
            PhoneNumber = phoneNumber,
            ExpiresAt = challenge.ExpiresAt,
            ResendAfterSeconds = await SettingProvider.GetAsync<int>(SahamSettings.Auth.OtpResendSeconds)
        };
    }
}
