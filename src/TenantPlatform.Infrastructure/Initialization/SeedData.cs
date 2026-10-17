using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Initialization;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Initialization;
public sealed record HostBootstrapEnvironment(bool IsDevelopment) : IBootstrapEnvironment;
public sealed class PlatformSeedData(IDbContextFactory<TenantPlatformDbContext> factory) : IPlatformSeedData
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PlatformDataInitializer.InitializeAsync(db, ct);
    }
}
public sealed class DemoSeedData(IDbContextFactory<TenantPlatformDbContext> factory, PasswordService passwords) : IDemoSeedData
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Serialize deterministic seed identities across starting hosts, preserving the existing
        // per-step SaveChanges boundaries and retry behavior of the fixed demo data builder.
        await db.Database.OpenConnectionAsync(ct);
        var acquired = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(hashtextextended('TenantPlatform:DemoSeed', 0))", ct); acquired = true;
            await DemoDataInitializer.InitializeAsync(db, passwords, ct);
        }
        finally
        {
            if (acquired) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(hashtextextended('TenantPlatform:DemoSeed', 0))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
