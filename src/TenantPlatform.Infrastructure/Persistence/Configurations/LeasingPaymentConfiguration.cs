using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class LeasingFinancingRevisionConfiguration : IEntityTypeConfiguration<LeasingFinancingRevision>
{
    public void Configure(EntityTypeBuilder<LeasingFinancingRevision> b)
    {
        b.ToTable("leasing_financing_revisions"); b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.AccountId,x.Id});
        b.Property(x=>x.SnapshotJson).HasColumnType("jsonb"); b.Property(x=>x.Reason).HasMaxLength(2000);
        b.HasIndex(x=>new{x.AccountId,x.AcquisitionId,x.EffectiveFrom}).IsUnique();
        b.HasIndex(x=>new{x.AccountId,x.AcquisitionId}).IsUnique().HasFilter("\"EffectiveFrom\" IS NULL");
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingPaymentPlanConfiguration : IEntityTypeConfiguration<LeasingPaymentPlan>
{
    public void Configure(EntityTypeBuilder<LeasingPaymentPlan> b)
    {
        b.ToTable("leasing_payment_plans"); b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.AccountId,x.Id});
        b.HasIndex(x=>new{x.AccountId,x.AcquisitionId,x.Version}).IsUnique();
        b.HasIndex(x=>new{x.AccountId,x.AcquisitionId}).IsUnique().HasFilter("\"Status\" = 2");
        b.Property(x=>x.Currency).HasMaxLength(3); b.Property(x=>x.ContentHash).HasMaxLength(64); b.Property(x=>x.Notes).HasMaxLength(10000); b.Property(x=>x.Revision).IsConcurrencyToken();
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingDocument>().WithMany().HasForeignKey(x=>x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingInstallmentConfiguration : IEntityTypeConfiguration<LeasingInstallment>
{
    public void Configure(EntityTypeBuilder<LeasingInstallment> b)
    {
        b.ToTable("leasing_installments"); b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.AccountId,x.Id});
        b.HasIndex(x=>new{x.AccountId,x.AcquisitionId,x.Reference}).IsUnique(); b.Property(x=>x.Reference).HasMaxLength(100);
        b.Property(x=>x.ControlReason).HasMaxLength(2000); b.Property(x=>x.Revision).IsConcurrencyToken();
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingPlanTermConfiguration : IEntityTypeConfiguration<LeasingPlanTerm>
{
    public void Configure(EntityTypeBuilder<LeasingPlanTerm> b)
    {
        b.ToTable("leasing_plan_terms",t=>t.HasCheckConstraint("CK_payment_term_totals","(\"Net\" IS NULL OR \"Net\" >= 0) AND (\"Vat\" IS NULL OR \"Vat\" >= 0) AND (\"Gross\" IS NULL OR \"Gross\" >= 0) AND (\"Net\" IS NULL OR \"Vat\" IS NULL OR \"Gross\" IS NULL OR \"Gross\" = \"Net\" + \"Vat\")"));
        b.HasKey(x=>x.Id); b.HasIndex(x=>new{x.AccountId,x.PlanId,x.InstallmentId}).IsUnique();
        b.Property(x=>x.Reference).HasMaxLength(100); b.Property(x=>x.ReviewReason).HasMaxLength(2000); b.Property(x=>x.ObligationDocumentReference).HasMaxLength(500);
        foreach(var name in new[]{"Net","Vat","Gross","CapitalComponent","InterestComponent","FeeComponent"}) b.Property<decimal?>(name).HasPrecision(20,2);
        b.HasOne<LeasingPaymentPlan>().WithMany(x=>x.Terms).HasForeignKey(x=>new{x.AccountId,x.PlanId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingInstallment>().WithMany().HasForeignKey(x=>new{x.AccountId,x.InstallmentId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingFinancingRevision>().WithMany().HasForeignKey(x=>new{x.AccountId,x.FinancingRevisionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingPaymentAllocationConfiguration : IEntityTypeConfiguration<LeasingPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<LeasingPaymentAllocation> b)
    {
        b.ToTable("leasing_payment_allocations",t=>t.HasCheckConstraint("CK_payment_allocation_positive","\"Net\" >= 0 AND \"Vat\" >= 0 AND \"Net\" + \"Vat\" > 0"));
        b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.AccountId,x.Id}); b.Property(x=>x.Net).HasPrecision(20,2); b.Property(x=>x.Vat).HasPrecision(20,2);
        b.HasOne<LeasingInvoice>().WithMany().HasForeignKey(x=>new{x.AccountId,x.InvoiceId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingInstallment>().WithMany().HasForeignKey(x=>new{x.AccountId,x.InstallmentId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingPaymentAllocation>().WithMany().HasForeignKey(x=>new{x.AccountId,x.CreditedAllocationId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingPaymentEventConfiguration : IEntityTypeConfiguration<LeasingPaymentEvent>
{
    public void Configure(EntityTypeBuilder<LeasingPaymentEvent> b)
    {
        b.ToTable("leasing_payment_events"); b.HasKey(x=>x.Id); b.Property(x=>x.Action).HasMaxLength(50); b.Property(x=>x.Reason).HasMaxLength(2000);
        b.Property(x=>x.BeforeJson).HasColumnType("jsonb");b.Property(x=>x.AfterJson).HasColumnType("jsonb");
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingInvoice>().WithMany().HasForeignKey(x=>new{x.AccountId,x.InvoiceId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
