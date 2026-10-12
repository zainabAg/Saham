using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Notifications;

/* Keeps the push token list honest: a token belongs to whoever registered it last, and tokens
 * FCM reports as dead (app uninstalled, token rotated) are removed so they are not tried again. */
public class UserDeviceManager : DomainService
{
    private readonly IRepository<UserDevice, Guid> _deviceRepository;

    public UserDeviceManager(IRepository<UserDevice, Guid> deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task<UserDevice> RegisterAsync(Guid userId, string token, DevicePlatform platform)
    {
        token = token.Trim();
        var device = await _deviceRepository.FirstOrDefaultAsync(d => d.Token == token);
        if (device == null)
        {
            return await _deviceRepository.InsertAsync(new UserDevice(GuidGenerator.Create(), userId, token, platform, Clock.Now));
        }

        device.Assign(userId, platform, Clock.Now);
        return await _deviceRepository.UpdateAsync(device);
    }

    /* Only the owner removes a token (sign-out); someone else's token is left alone. */
    public async Task UnregisterAsync(Guid userId, string token)
    {
        token = token.Trim();
        await _deviceRepository.DeleteAsync(d => d.Token == token && d.UserId == userId);
    }

    public async Task<List<string>> GetTokensAsync(Guid userId)
    {
        return (await _deviceRepository.GetListAsync(d => d.UserId == userId))
            .Select(d => d.Token)
            .ToList();
    }

    public async Task RemoveTokensAsync(IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count > 0)
        {
            await _deviceRepository.DeleteAsync(d => tokens.Contains(d.Token));
        }
    }
}
