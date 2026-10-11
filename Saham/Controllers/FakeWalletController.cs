using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Saham.Entities.Payments;
using Saham.Integrations.Wallet;
using Volo.Abp.AspNetCore.Mvc;

namespace Saham.Controllers;

/* The fake provider's "payment page" — development only (or Wallet:Fake:EnablePayPage).
 * POST /fake-wallet/pay/{paymentId}?outcome=succeed|fail|silent[&amount=N][&deliver=false]
 *   succeed / fail — the provider builds a signed webhook and delivers it to our handler
 *                    (deliver=false only returns it, so a test can post it to the real endpoint);
 *   silent         — the customer paid but no webhook comes: only the status poll finds it (UC-05 A1);
 *   amount         — a different amount, to test the mismatch path (E5). */
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("fake-wallet")]
[ApiExplorerSettings(IgnoreApi = true)]
public class FakeWalletController : AbpController
{
    private readonly FakeWalletProvider _provider;
    private readonly PaymentManager _paymentManager;
    private readonly IWebHostEnvironment _environment;
    private readonly WalletOptions _options;

    public FakeWalletController(FakeWalletProvider provider, PaymentManager paymentManager, IWebHostEnvironment environment,
        IOptions<WalletOptions> options)
    {
        _provider = provider;
        _paymentManager = paymentManager;
        _environment = environment;
        _options = options.Value;
    }

    [HttpPost("pay/{paymentId:guid}")]
    public async Task<IActionResult> PayAsync(Guid paymentId, string outcome = "succeed", long? amount = null, bool deliver = true)
    {
        if (!_environment.IsDevelopment() && !_options.Fake.EnablePayPage)
        {
            return NotFound();
        }

        if (outcome == "silent")
        {
            _provider.SimulateSilentPayment(paymentId);
            return Ok(new { outcome });
        }

        var (body, signature) = _provider.SimulateCustomerPayment(paymentId, outcome != "fail", amount);
        var result = deliver
            ? (await _paymentManager.HandleWebhookAsync(_provider.Name, body, signature)).ToString()
            : null;
        return Ok(new { outcome, body, signature, webhook = result });
    }
}
