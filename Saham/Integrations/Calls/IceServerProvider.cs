using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace Saham.Integrations.Calls;

/* Configuration section "Calls": where the two apps meet for the audio (SRS 4: WebRTC + the
 * company's own TURN server, coturn — no telecom provider, no phone number on either side).
 * TurnSharedSecret is coturn's "static-auth-secret" (keep it in appsettings.secrets.json). */
public class CallOptions
{
    public List<string> StunUrls { get; set; } = new() { "stun:stun.l.google.com:19302" };

    public List<string> TurnUrls { get; set; } = new();

    public string? TurnSharedSecret { get; set; }

    public int CredentialTtlMinutes { get; set; } = 30;
}

public record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);

/* Short-lived TURN credentials in coturn's REST format: username = "<expiry unix>:<user id>",
 * credential = base64(HMAC-SHA1(secret, username)). Nothing to store, nothing reusable for long. */
public class IceServerProvider : ITransientDependency
{
    private readonly CallOptions _options;
    private readonly IClock _clock;

    public IceServerProvider(IOptions<CallOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public List<IceServer> GetFor(Guid userId)
    {
        var servers = new List<IceServer>();
        if (_options.StunUrls.Count > 0)
        {
            servers.Add(new IceServer(_options.StunUrls));
        }

        if (_options.TurnUrls.Count > 0 && !string.IsNullOrWhiteSpace(_options.TurnSharedSecret))
        {
            var expiry = new DateTimeOffset(_clock.Now.AddMinutes(_options.CredentialTtlMinutes)).ToUnixTimeSeconds();
            var username = $"{expiry}:{userId:N}";
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.TurnSharedSecret));
            var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
            servers.Add(new IceServer(_options.TurnUrls.ToList(), username, credential));
        }

        return servers;
    }
}
