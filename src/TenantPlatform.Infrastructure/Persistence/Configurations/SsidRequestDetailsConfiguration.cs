using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class SsidRequestDetailsConfiguration
    : IEntityTypeConfiguration<SsidRequestDetails>
{
    public void Configure(EntityTypeBuilder<SsidRequestDetails> builder)
    {
        builder.ToTable("ssid_request_details");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.RequestedName)
            .HasMaxLength(32);

        builder.Property(x => x.RequestedVlanId);

        builder.Property(x => x.RequestedSecurityType)
            .HasConversion<int>();

        builder.Property(x => x.RequestedIsBroadcast);

        builder.HasOne<NetworkEnvironment>()
            .WithMany()
            .HasForeignKey(x => x.NetworkEnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceRequest>()
            .WithOne()
            .HasForeignKey<SsidRequestDetails>(
                x => x.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<NetworkSsid>()
            .WithMany()
            .HasForeignKey(x => x.ExistingNetworkSsidId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ServiceRequestId)
            .IsUnique();

        builder.HasIndex(x => x.ExistingNetworkSsidId);
        builder.HasIndex(x => x.NetworkEnvironmentId);
    }
}
