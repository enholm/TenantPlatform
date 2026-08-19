using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceDefinitionProviderConfiguration
    : IEntityTypeConfiguration<ServiceDefinitionProvider>
{
    public void Configure(
        EntityTypeBuilder<ServiceDefinitionProvider> builder)
    {
        builder.ToTable("service_definition_providers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IntegrationType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.RequestEmailAddress)
            .HasMaxLength(320);

        builder.Property(x => x.IsDefault)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.ServiceProviderOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.ServiceDefinitionId,
            x.ServiceProviderOrganizationId
        })
        .IsUnique();

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.ServiceProviderOrganizationId);
    }
}

