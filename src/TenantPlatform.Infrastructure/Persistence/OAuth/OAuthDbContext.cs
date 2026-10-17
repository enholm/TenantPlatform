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
        var server = builder.Entity<TenantPlatform.Core.Identity.McpServerDefinition>();
        server.ToTable("mcp_servers"); server.HasKey(x => x.Id); server.HasIndex(x => x.AccountId);
        server.Property(x => x.Name).HasMaxLength(100); server.Property(x => x.ProfileId).HasMaxLength(100);
        server.Property(x => x.ProfileVersion).HasMaxLength(100); server.Property(x => x.Revision).IsConcurrencyToken();
        var tool = builder.Entity<TenantPlatform.Core.Identity.McpDiscoveredTool>();
        tool.ToTable("mcp_tools"); tool.HasKey(x => new { x.ConnectionId, x.Name });
        tool.Property(x => x.Name).HasMaxLength(128); tool.Property(x => x.Version).HasMaxLength(100);
        tool.Property(x => x.Description).HasMaxLength(4000); tool.Property(x => x.InputSchema).HasMaxLength(16000); tool.Property(x => x.OutputSchema).HasMaxLength(16000);
        tool.HasOne<TenantPlatform.Core.Identity.McpServerDefinition>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
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
