using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceRequestMessageConfiguration
    : IEntityTypeConfiguration<ServiceRequestMessage>
{
    public void Configure(
        EntityTypeBuilder<ServiceRequestMessage> builder)
    {
        builder.ToTable("service_request_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Direction)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.FromAddress)
            .HasMaxLength(320);

        builder.Property(x => x.ToAddress)
            .HasMaxLength(320);

        builder.Property(x => x.Subject)
            .HasMaxLength(500);

        builder.Property(x => x.Body)
            .HasColumnType("text");

        builder.Property(x => x.ExternalMessageId)
            .HasMaxLength(500);

        builder.Property(x => x.ExternalThreadId)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(x => x.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ServiceRequestId);

        builder.HasIndex(x => new
        {
            x.ServiceRequestId,
            x.CreatedAt
        });

        builder.HasIndex(x => x.ExternalMessageId);
    }
}

