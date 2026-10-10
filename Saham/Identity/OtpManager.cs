using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Saham.Entities.Identity;
using Saham.Localization;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace Saham.Identity;

/* Issues and verifies login codes (DEC-01, ASM-13). */
public class OtpManager : DomainService
{
    private readonly IRepository<OtpChallenge, Guid> _challengeRepository;
    private readonly ISmsSender _smsSender;
    private readonly ISettingProvider _settingProvider;
    private readonly IStringLocalizer<SahamResource> _localizer;

    public OtpManager(
        IRepository<OtpChallenge, Guid> challengeRepository,
        ISmsSender smsSender,
        ISettingProvider settingProvider,
        IStringLocalizer<SahamResource> localizer)
    {
        _challengeRepository = challengeRepository;
        _smsSender = smsSender;
        _settingProvider = settingProvider;
        _localizer = localizer;
    }

    public async Task<OtpChallenge> SendAsync(string phoneNumber, OtpPurpose purpose)
    {
        var now = Clock.Now;
        var resendSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Auth.OtpResendSeconds);
        var lastSentAt = now.AddSeconds(-resendSeconds);

        if (await _challengeRepository.AnyAsync(x =>
                x.PhoneNumber == phoneNumber && x.Purpose == purpose && x.CreationTime > lastSentAt))
        {
            throw new BusinessException(SahamErrorCodes.OtpTooManyRequests)
                .WithData("seconds", resendSeconds);
        }

        var length = await _settingProvider.GetAsync<int>(SahamSettings.Auth.OtpLength);
        var lifetime = await _settingProvider.GetAsync<int>(SahamSettings.Auth.OtpLifetimeSeconds);
        var code = GenerateCode(length);

        var id = GuidGenerator.Create();
        var challenge = new OtpChallenge(id, phoneNumber, Hash(id, code), purpose, now, now.AddSeconds(lifetime));
        await _challengeRepository.InsertAsync(challenge);

        await _smsSender.SendAsync(new SmsMessage(phoneNumber, _localizer["Sms:LoginCode", code]));

        return challenge;
    }

    /* Returns the outcome instead of throwing, so a failed attempt is still saved. */
    public async Task<OtpVerificationResult> VerifyAsync(string phoneNumber, string code, OtpPurpose purpose)
    {
        var queryable = await _challengeRepository.GetQueryableAsync();
        var challenge = await AsyncExecuter.FirstOrDefaultAsync(queryable
            .Where(x => x.PhoneNumber == phoneNumber && x.Purpose == purpose && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreationTime));

        if (challenge == null)
        {
            return OtpVerificationResult.Invalid;
        }

        var now = Clock.Now;
        if (challenge.IsExpired(now))
        {
            return OtpVerificationResult.Expired;
        }

        var maxAttempts = await _settingProvider.GetAsync<int>(SahamSettings.Auth.OtpMaxAttempts);
        if (challenge.Attempts >= maxAttempts)
        {
            return OtpVerificationResult.Locked;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.CodeHash),
                Encoding.ASCII.GetBytes(Hash(challenge.Id, code.Trim()))))
        {
            challenge.RegisterFailedAttempt();
            await _challengeRepository.UpdateAsync(challenge);
            return challenge.Attempts >= maxAttempts ? OtpVerificationResult.Locked : OtpVerificationResult.Invalid;
        }

        challenge.Consume(now);
        await _challengeRepository.UpdateAsync(challenge);
        return OtpVerificationResult.Success;
    }

    private static string GenerateCode(int length)
    {
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, max).ToString().PadLeft(length, '0');
    }

    private static string Hash(Guid challengeId, string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));
    }
}

public enum OtpVerificationResult
{
    Success,
    Invalid,
    Expired,
    Locked
}
