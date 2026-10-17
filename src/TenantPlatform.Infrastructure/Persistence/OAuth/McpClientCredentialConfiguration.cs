using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.OAuth;

public static class McpClientCredentialConfiguration
{
    public static void Configure(EntityTypeBuilder<McpClientCredential> b)
    {
        b.ToTable("mcp_client_credentials"); b.HasKey(x => new { x.UserId, x.ConnectionId });
        b.Property(x => x.ProfileVersion).HasMaxLength(100);
        b.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Issuer).HasMaxLength(2048).IsRequired();
        b.Property(x => x.Resource).HasMaxLength(2048).IsRequired();
        b.Property(x => x.GrantId).HasMaxLength(200);
    }
}
