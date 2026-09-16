using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementAccessConfiguration : IEntityTypeConfiguration<AgreementAccess>
{
    public void Configure(EntityTypeBuilder<AgreementAccess> b)
    {
        b.ToTable("agreement_access", t => t.HasCheckConstraint("CK_agreement_access_level", "\"Level\" IN (1,2)"));
        b.HasKey(x => x.Id);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.UserId });
    }
}
