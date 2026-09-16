using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementConfiguration : IEntityTypeConfiguration<Agreement>
{
    public void Configure(EntityTypeBuilder<Agreement> b)
    {
        b.ToTable("agreements", t =>
        {
            t.HasCheckConstraint("CK_agreements_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint("CK_agreements_renewal", "\"RenewalMonths\" IS NULL OR (\"AutoRenew\" AND \"RenewalMonths\" > 0)");
            t.HasCheckConstraint("CK_agreements_unit_building", "\"UnitId\" IS NULL OR \"BuildingId\" IS NOT NULL");
            t.HasCheckConstraint("CK_agreements_status", "\"Status\" IN (1,2,3,4)");
            t.HasCheckConstraint("CK_agreements_type", "\"Type\" IN (1,2,3,4,5)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Terms).HasMaxLength(10000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.CounterpartyOrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Building>().WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.Status, x.Type });
        b.HasIndex(x => new { x.AccountId, x.OwnerUserId });
        b.HasIndex(x => new { x.AccountId, x.NoticeDeadline });
    }
}
