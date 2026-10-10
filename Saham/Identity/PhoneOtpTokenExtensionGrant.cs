using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Saham.Entities.Identity;
using Saham.Localization;
using Volo.Abp;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace Saham.Identity;

/* Phone + OTP login (DEC-01, D2):
 *   1. POST /api/app/phone-auth/send-otp { phoneNumber }   -> code goes to SmsLog
 *   2. POST /connect/token  grant_type=phone_otp & phone_number & code
 *                           & client_id=Saham_App & scope=Saham offline_access
 * Unknown numbers are registered as customers on first successful login. */
public class PhoneOtpTokenExtensionGrant : ITokenExtensionGrant
{
    public const string ExtensionGrantName = "phone_otp";
    public const string PhoneNumberParameter = "phone_number";
    public const string CodeParameter = "code";

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var services = context.HttpContext.RequestServices;
        var localizer = services.GetRequiredService<IStringLocalizer<SahamResource>>();

        var code = context.Request.GetParameter(CodeParameter)?.ToString();
        string phoneNumber;
        try
        {
            phoneNumber = PhoneNumberNormalizer.Normalize(context.Request.GetParameter(PhoneNumberParameter)?.ToString());
        }
        catch (BusinessException)
        {
            return Error(localizer[SahamErrorCodes.InvalidPhoneNumber]);
        }

        if (code.IsNullOrWhiteSpace())
        {
            return Error(localizer[SahamErrorCodes.OtpInvalid]);
        }

        IdentityUser? user = null;
        string? errorCode = null;

        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var result = await services.GetRequiredService<OtpManager>()
                .VerifyAsync(phoneNumber, code!, OtpPurpose.Login);

            errorCode = result switch
            {
                OtpVerificationResult.Success => null,
                OtpVerificationResult.Expired => SahamErrorCodes.OtpExpired,
                OtpVerificationResult.Locked => SahamErrorCodes.OtpLocked,
                _ => SahamErrorCodes.OtpInvalid
            };

            if (errorCode == null)
            {
                user = await services.GetRequiredService<PhoneUserManager>().FindOrRegisterCustomerAsync(phoneNumber);
                if (!user.IsActive)
                {
                    errorCode = SahamErrorCodes.UserDisabled;
                    user = null;
                }
            }

            // Completed even on failure, so the failed attempt counter is saved.
            await uow.CompleteAsync();
        }

        if (user == null)
        {
            return Error(localizer[errorCode!]);
        }

        var principal = await services.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
        var scopes = context.Request.GetScopes();
        principal.SetScopes(scopes);
        principal.SetResources(await GetResourcesAsync(services, scopes));

        await services.GetRequiredService<AbpOpenIddictClaimsPrincipalManager>().HandleAsync(context.Request, principal);

        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
    }

    private static ForbidResult Error(string description)
    {
        return new ForbidResult(
            new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }));
    }

    private static async Task<IEnumerable<string>> GetResourcesAsync(IServiceProvider services, ImmutableArray<string> scopes)
    {
        var resources = new List<string>();
        if (!scopes.Any())
        {
            return resources;
        }

        await foreach (var resource in services.GetRequiredService<IOpenIddictScopeManager>().ListResourcesAsync(scopes))
        {
            resources.Add(resource);
        }

        return resources;
    }
}
