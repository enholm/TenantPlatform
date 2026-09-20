using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementIndexSelectionConfiguration : IEntityTypeConfiguration<AgreementIndexSelection>
{
    public void Configure(EntityTypeBuilder<AgreementIndexSelection> b)
    {
        b.ToTable("agreement_index_selections"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.Sequence }).IsUnique();
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementIndex>().WithMany().HasForeignKey(x => new { x.AccountId, x.IndexId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
