using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceRequestFieldValueConfiguration
    : IEntityTypeConfiguration<ServiceRequestFieldValue>
{
    public void Configure(
        EntityTypeBuilder<ServiceRequestFieldValue> builder)
    {
        builder.ToTable("service_request_field_values");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value)
            .HasMaxLength(4000);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(x => x.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ServiceDefinitionField>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionFieldId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.ServiceRequestId,
            x.ServiceDefinitionFieldId
        })
        .IsUnique();

        builder.HasIndex(x => x.ServiceDefinitionFieldId);
    }
}

