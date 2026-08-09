using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class NetworkSsidConfiguration
    : IEntityTypeConfiguration<NetworkSsid>
{
    public void Configure(EntityTypeBuilder<NetworkSsid> builder)
    {
        builder.ToTable("network_ssids");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.VlanId);

        builder.Property(x => x.SecurityType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsBroadcast)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.TenantOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.TenantOrganizationId);
        builder.HasIndex(x => x.UnitId);

        builder.HasIndex(x => new
        {
            x.AccountId,
            x.Name
        })
        .IsUnique();
    }
}
