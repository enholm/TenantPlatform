using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenantPlatform.Infrastructure.Persistence.OAuth;

public sealed class OAuthDbContext(DbContextOptions<OAuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("oauth");
        builder.UseOpenIddict();
        McpClientCredentialConfiguration.Configure(builder.Entity<TenantPlatform.Core.Identity.McpClientCredential>());
    }
}

// Tooling only; migration generation cannot resolve a development/production connection string.
public sealed class OAuthDesignFactory : IDesignTimeDbContextFactory<OAuthDbContext>
{
    public OAuthDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OAuthDbContext>()
        .UseNpgsql("Host=localhost;Database=oauth_design_only", x => x.MigrationsHistoryTable("__EFMigrationsHistory", "oauth"))
        .UseOpenIddict().Options);
}
