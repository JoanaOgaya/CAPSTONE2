using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FOMS.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    public static async Task ApplyMigrationsAsync(DbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (context.Database.IsRelational())
        {
            try
            {
                logger.LogInformation("Applying database migrations...");
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied successfully.");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Fatal Startup Error: Database migration failed. Schema is not ready.");
                throw new InvalidOperationException("Fatal Startup Error: Database migration failed. Cannot start service with unmigrated schema.", ex);
            }
        }
        else
        {
            logger.LogInformation("Non-relational database provider in use ({Provider}); skipping relational migrations.", context.Database.ProviderName);
        }
    }
}
