using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class GeographicAreaConfiguration : IEntityTypeConfiguration<GeographicArea>
{
    public void Configure(EntityTypeBuilder<GeographicArea> builder)
    {
        builder.ToTable("geographic_areas");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.AccountId, x.Id });
        builder.HasOne<GeographicArea>().WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ParentId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => new { x.AccountId, x.Name });
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
