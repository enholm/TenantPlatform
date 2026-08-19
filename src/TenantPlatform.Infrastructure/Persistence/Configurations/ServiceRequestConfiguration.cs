using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration
    : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.SubmittedAt);

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.Title)
            .HasMaxLength(300);

        builder.Property(x => x.Comment)
            .HasMaxLength(4000);

        builder.Property(x => x.ReplyToken)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.RequesterOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Building>()
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.AssignedServiceProviderOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.ServiceDefinitionId);
        builder.HasIndex(x => x.RequesterUserId);
        builder.HasIndex(x => x.RequesterOrganizationId);
        builder.HasIndex(x => x.BuildingId);
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AssignedServiceProviderOrganizationId);

        builder.HasIndex(x => x.ReplyToken)
            .IsUnique();

        builder.HasIndex(x => new
        {
            x.AccountId,
            x.CreatedAt
        });
    }
}