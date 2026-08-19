using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceDefinitionFieldTranslationConfiguration
    : IEntityTypeConfiguration<ServiceDefinitionFieldTranslation>
{
    public void Configure(
        EntityTypeBuilder<ServiceDefinitionFieldTranslation> builder)
    {
        builder.ToTable("service_definition_field_translations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LanguageCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Label)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.HelpText)
            .HasMaxLength(1000);

        builder.Property(x => x.Placeholder)
            .HasMaxLength(200);

        builder.HasOne<ServiceDefinitionField>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new
        {
            x.ServiceDefinitionFieldId,
            x.LanguageCode
        })
        .IsUnique();
    }
}

