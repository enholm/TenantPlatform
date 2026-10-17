using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Initialization;

public static class DatabaseInitializer
{
public static async Task MigrateSchemaAsync(
    TenantPlatformDbContext dbContext,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    try
    {
        logger.LogInformation("Initializing database.");

        var pendingMigrations =
            (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken))
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            logger.LogInformation("Database schema is already up to date.");
        }
        else
        {
            logger.LogInformation(
                "Applying {MigrationCount} pending database migration(s): {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));

            await dbContext.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Database migrations completed.");
        }

        logger.LogInformation("Database initialization completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(
            ex,
            "Database initialization failed. The application will not start.");

        throw;
    }
}}
