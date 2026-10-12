using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Notifications;
using Saham.Services.Dtos.Account;
using Volo.Abp.Users;

namespace Saham.Services.Account;

/* Push registration for every app (customer, driver, restaurant tablet): after sign-in and on
 * every Firebase token refresh the app calls register; on sign-out it calls unregister, so the
 * phone stops receiving the previous user's notifications. */
[Authorize]
public class DeviceAppService : SahamAppService, IDeviceAppService
{
    private readonly UserDeviceManager _deviceManager;

    public DeviceAppService(UserDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
    }

    public async Task RegisterAsync(RegisterDeviceDto input)
    {
        await _deviceManager.RegisterAsync(CurrentUser.GetId(), input.Token, input.Platform);
    }

    public async Task UnregisterAsync(UnregisterDeviceDto input)
    {
        await _deviceManager.UnregisterAsync(CurrentUser.GetId(), input.Token);
    }
}
