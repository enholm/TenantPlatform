using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class LeasingFrameworkConfiguration : IEntityTypeConfiguration<LeasingFramework>
{
    public void Configure(EntityTypeBuilder<LeasingFramework> b)
    {
        b.ToTable("leasing_frameworks", t =>
        {
            t.HasCheckConstraint("CK_leasing_framework_period", "\"AcquisitionTo\" >= \"AcquisitionFrom\"");
            t.HasCheckConstraint("CK_leasing_framework_limit", "\"Limit\" >= 0");
            t.HasCheckConstraint("CK_leasing_framework_status", "\"Status\" IN (1,2,3)");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Number).HasMaxLength(100);
        b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.Notes).HasMaxLength(10000);
        b.Property(x => x.Limit).HasPrecision(20, 2); b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.FinanceOrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsOne(x => x.Terms, LeasingMapping.Terms);
        b.Navigation(x => x.Terms).IsRequired();
        b.HasIndex(x => new { x.AccountId, x.Status });
    }
}
public sealed class LeasingAcquisitionConfiguration : IEntityTypeConfiguration<LeasingAcquisition>
{
    public void Configure(EntityTypeBuilder<LeasingAcquisition> b)
    {
        b.ToTable("leasing_acquisitions", t =>
        {
            t.HasCheckConstraint("CK_leasing_acquisition_dates", "\"EndDate\" > \"PurchaseDate\"");
            t.HasCheckConstraint("CK_leasing_acquisition_amounts", "\"NetTotal\" >= 0 AND \"VatTotal\" >= 0 AND \"GrossTotal\" = \"NetTotal\" + \"VatTotal\" AND \"FinancedAmount\" >= 0");
            t.HasCheckConstraint("CK_leasing_acquisition_status", "\"Status\" IN (1,2,3)");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.InvoiceNumber).HasMaxLength(100); b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Notes).HasMaxLength(10000); b.Property(x => x.Revision).IsConcurrencyToken();
        foreach (var name in new[] { "NetTotal", "VatTotal", "GrossTotal", "FinancedAmount" }) b.Property<decimal>(name).HasPrecision(20, 2);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x => new { x.AccountId, x.FrameworkId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.SupplierOrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.FinanceOrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsOne(x => x.Terms, LeasingMapping.Terms); b.Navigation(x => x.Terms).IsRequired();
        b.HasIndex(x => new { x.AccountId, x.FrameworkId, x.Status });
    }
}
public sealed class LeasingItemConfiguration : IEntityTypeConfiguration<LeasingItem>
{
    public void Configure(EntityTypeBuilder<LeasingItem> b)
    {
        b.ToTable("leasing_items", t => t.HasCheckConstraint("CK_leasing_item_positive", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"VatPercent\" >= 0 AND \"VatPercent\" <= 100"));
        b.HasKey(x => x.Id); b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.ItemNumber).HasMaxLength(100);
        b.Property(x => x.Quantity).HasPrecision(18, 4); b.Property(x => x.UnitPrice).HasPrecision(18, 4); b.Property(x => x.VatPercent).HasPrecision(7, 4);
        b.HasOne<LeasingAcquisition>().WithMany(x => x.Items).HasForeignKey(x => new { x.AccountId, x.AcquisitionId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingDocumentConfiguration : IEntityTypeConfiguration<LeasingDocument>
{
    public void Configure(EntityTypeBuilder<LeasingDocument> b)
    {
        b.ToTable("leasing_documents", t => t.HasCheckConstraint("CK_leasing_document_parent", "(\"FrameworkId\" IS NULL) <> (\"AcquisitionId\" IS NULL)"));
        b.HasKey(x => x.Id); b.Property(x => x.FileName).HasMaxLength(255); b.Property(x => x.StorageKey).HasMaxLength(500); b.Property(x => x.MediaType).HasMaxLength(200);
        b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x => new { x.AccountId, x.FrameworkId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x => new { x.AccountId, x.AcquisitionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingHistoryConfiguration : IEntityTypeConfiguration<LeasingHistory>
{
    public void Configure(EntityTypeBuilder<LeasingHistory> b)
    {
        b.ToTable("leasing_history", t => t.HasCheckConstraint("CK_leasing_history_parent", "(\"FrameworkId\" IS NULL) <> (\"AcquisitionId\" IS NULL)"));
        b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(50); b.Property(x => x.Reason).HasMaxLength(2000);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x => new { x.AccountId, x.FrameworkId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x => new { x.AccountId, x.AcquisitionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.RecordedUtc });
    }
}
internal static class LeasingMapping
{
    public static void Terms<T>(OwnedNavigationBuilder<T, LeasingTerms> b) where T : class
    {
        b.Property(x => x.AnnualRatePercent).HasPrecision(9, 4);
        b.Property(x => x.MarginPercentagePoints).HasPrecision(9, 4);
        b.Property(x => x.ReferenceRateName).HasMaxLength(100);
    }
}
