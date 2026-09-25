using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class OrganizationElementConfiguration : IEntityTypeConfiguration<OrganizationElement>
{
    public void Configure(EntityTypeBuilder<OrganizationElement> builder)
    {
        builder.ToTable("organization_elements");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.AccountId, x.Id });
        builder.HasOne<OrganizationElement>().WithMany()
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
