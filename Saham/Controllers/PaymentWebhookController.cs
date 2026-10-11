using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saham.Entities.Payments;
using Saham.Integrations.Wallet;
using Volo.Abp.AspNetCore.Mvc;

namespace Saham.Controllers;

/* The wallet provider's webhook (UC-05 step 5): anonymous, but every body must carry a valid
 * signature. The raw body is read as sent (the signature is over the exact bytes). Answers 200 to a
 * processed or repeated event (E3), so the provider stops retrying; 400 to a bad signature. */
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/payments/webhook")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PaymentWebhookController : AbpController
{
    private readonly PaymentManager _paymentManager;

    public PaymentWebhookController(PaymentManager paymentManager)
    {
        _paymentManager = paymentManager;
    }

    [HttpPost("{provider}")]
    public async Task<IActionResult> ReceiveAsync(string provider)
    {
        string body;
        // leaveOpen: the request body still belongs to ASP.NET Core (auditing reads it after the action).
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
        }

        var outcome = await _paymentManager.HandleWebhookAsync(provider, body, Request.Headers[FakeWalletProvider.SignatureHeader]);
        return outcome switch
        {
            WebhookOutcome.InvalidSignature => BadRequest(new { outcome = outcome.ToString() }),
            WebhookOutcome.UnknownProvider => NotFound(),
            _ => Ok(new { outcome = outcome.ToString() })
        };
    }
}
