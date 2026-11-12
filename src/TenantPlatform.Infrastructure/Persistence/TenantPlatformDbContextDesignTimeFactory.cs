using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TenantPlatform.Infrastructure.Persistence;

// EF tooling discovers this factory without building the Web host. No application
// configuration, secrets, services, initialization or database access is needed.
public sealed class TenantPlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantPlatformDbContext>
{
    public TenantPlatformDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<TenantPlatformDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=tenantplatform_design_time;Username=design_time")
            .AddInterceptors(new OfflineConnectionGuard()).Options);

    private sealed class OfflineConnectionGuard : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => throw new InvalidOperationException("The design-time context is offline; database connections are prohibited.");

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The design-time context is offline; database connections are prohibited.");
    }
}
