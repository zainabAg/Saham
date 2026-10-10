using Volo.Abp.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Saham.Data;

public class SahamDbSchemaMigrator : ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public SahamDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        
        /* We intentionally resolving the SahamDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<SahamDbContext>()
            .Database
            .MigrateAsync();

    }
}
