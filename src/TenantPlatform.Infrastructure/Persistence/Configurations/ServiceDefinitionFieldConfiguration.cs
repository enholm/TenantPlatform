using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceDefinitionFieldConfiguration
    : IEntityTypeConfiguration<ServiceDefinitionField>
{
    public void Configure(
        EntityTypeBuilder<ServiceDefinitionField> builder)
    {
        builder.ToTable("service_definition_fields");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.FieldType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsRequired)
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.Property(x => x.Options)
            .HasColumnName("options")
            .HasColumnType("jsonb");

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new
        {
            x.ServiceDefinitionId,
            x.Key
        })
        .IsUnique();

        builder.HasIndex(x => new
        {
            x.ServiceDefinitionId,
            x.SortOrder
        });
    }
}

