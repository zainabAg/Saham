using System;
using System.Collections.Concurrent;
using Volo.Abp.DependencyInjection;

namespace Saham.Hubs;

/* Which users have the app open right now (a live OrdersHub connection). Used by in-app calls:
 * a customer whose app is not connected cannot be rung, the driver asks ops instead (TD-03,
 * CUSTOMER_APP_OFFLINE). In memory, per server: running several servers needs a shared store
 * (Redis) together with a SignalR backplane. */
public class UserPresenceTracker : ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    public void Connected(Guid userId) => _connections.AddOrUpdate(userId, 1, (_, n) => n + 1);

    public void Disconnected(Guid userId)
    {
        if (_connections.AddOrUpdate(userId, 0, (_, n) => Math.Max(0, n - 1)) == 0)
        {
            _connections.TryRemove(new System.Collections.Generic.KeyValuePair<Guid, int>(userId, 0));
        }
    }

    public bool IsOnline(Guid userId) => _connections.TryGetValue(userId, out var n) && n > 0;
}
