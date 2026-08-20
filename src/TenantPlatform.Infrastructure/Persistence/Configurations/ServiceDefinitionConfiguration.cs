using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceDefinitionConfiguration
    : IEntityTypeConfiguration<ServiceDefinition>
{
    public void Configure(EntityTypeBuilder<ServiceDefinition> builder)
    {
        builder.ToTable("service_definitions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Category)
            .HasMaxLength(100);

        builder.Property(x => x.HandlerType)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.RequiresApproval)
            .IsRequired();

        builder.Property(x => x.IsBookableByTenant)
            .IsRequired();

        builder.Property(x => x.RequiresOccupancy)
            .IsRequired();

        builder.Property(x => x.EstimatedDurationMinutes);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.AccountId,
            x.Code
        })
        .IsUnique();

        builder.HasIndex(x => new
        {
            x.AccountId,
            x.Category
        });
    }
}
