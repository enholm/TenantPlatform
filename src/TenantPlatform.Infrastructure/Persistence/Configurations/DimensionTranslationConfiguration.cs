using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Dimensions;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class DimensionTranslationConfiguration : IEntityTypeConfiguration<DimensionTranslation>
{
    public void Configure(EntityTypeBuilder<DimensionTranslation> builder)
    {
        builder.ToTable("dimension_translations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => new { x.AccountId, x.DimensionId, x.LanguageCode }).IsUnique();
        builder.HasOne<Dimension>().WithMany(x => x.Translations)
            .HasForeignKey(x => new { x.AccountId, x.DimensionId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
