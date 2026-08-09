using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class NetworkEnvironmentConfiguration
    : IEntityTypeConfiguration<NetworkEnvironment>
{
    public void Configure(EntityTypeBuilder<NetworkEnvironment> builder)
    {
        builder.ToTable("network_environments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Vendor)
            .HasConversion<int>();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Building>()
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.BuildingId,
            x.Name
        }).IsUnique();
            }
}
