using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class ServiceDefinitionTranslationConfiguration
    : IEntityTypeConfiguration<ServiceDefinitionTranslation>
{
    public void Configure(
        EntityTypeBuilder<ServiceDefinitionTranslation> builder)
    {
        builder.ToTable("service_definition_translations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LanguageCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(x => x.ServiceDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ServiceDefinitionId);

        builder.HasIndex(x => new
        {
            x.ServiceDefinitionId,
            x.LanguageCode
        })
        .IsUnique();
    }
}
