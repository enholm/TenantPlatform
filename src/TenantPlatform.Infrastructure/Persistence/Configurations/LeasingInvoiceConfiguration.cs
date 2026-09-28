using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;
public sealed class LeasingInvoiceConfiguration : IEntityTypeConfiguration<LeasingInvoice>
{
    public void Configure(EntityTypeBuilder<LeasingInvoice> b)
    {
        b.ToTable("leasing_invoices"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.Revision).IsConcurrencyToken(); b.Property(x => x.FileHash).HasMaxLength(64);
        b.Property(x => x.FileName).HasMaxLength(200); b.Property(x => x.StorageKey).HasMaxLength(500); b.Property(x => x.MediaType).HasMaxLength(100);
        b.Property(x => x.Number).HasMaxLength(100); b.Property(x => x.SupplierIdentity).HasMaxLength(200); b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Net).HasPrecision(20,2); b.Property(x => x.Vat).HasPrecision(20,2);
        b.Property(x => x.ReviewJson).HasColumnType("jsonb"); b.Property(x => x.ApprovedJson).HasColumnType("jsonb");
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x => new { x.AccountId, x.AcquisitionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.FileHash }); b.HasIndex(x => new { x.AccountId, x.SupplierIdentity, x.Kind, x.Number });
        b.HasIndex(x => new { x.Processing, x.ProcessingStartedUtc });
        b.HasOne<LeasingInvoice>().WithMany().HasForeignKey(x => new { x.AccountId, x.OriginalInvoiceId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.Category, x.Status });
    }
}
public sealed class LeasingInvoiceLineConfiguration : IEntityTypeConfiguration<LeasingInvoiceLine>
{
    public void Configure(EntityTypeBuilder<LeasingInvoiceLine> b)
    {
        b.ToTable("leasing_invoice_lines"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.SourceLineId).HasMaxLength(200); b.Property(x => x.Quantity).HasPrecision(18,4); b.Property(x => x.Net).HasPrecision(20,2); b.Property(x => x.Vat).HasPrecision(20,2);
        b.HasOne<LeasingInvoice>().WithMany(x => x.Lines).HasForeignKey(x => new { x.AccountId, x.InvoiceId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingItem>().WithMany().HasForeignKey(x => new { x.AccountId, x.ItemId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingInvoiceLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.CreditedLineId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingInvoiceInterpretationConfiguration : IEntityTypeConfiguration<LeasingInvoiceInterpretation>
{
    public void Configure(EntityTypeBuilder<LeasingInvoiceInterpretation> b)
    {
        b.ToTable("leasing_invoice_interpretations"); b.HasKey(x => x.Id);
        b.Property(x => x.ResultJson).HasColumnType("jsonb"); b.Property(x => x.WarningsJson).HasColumnType("jsonb");
        b.HasOne<LeasingInvoice>().WithMany().HasForeignKey(x => new { x.AccountId, x.InvoiceId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingInvoiceHistoryConfiguration : IEntityTypeConfiguration<LeasingInvoiceHistory>
{
    public void Configure(EntityTypeBuilder<LeasingInvoiceHistory> b)
    {
        b.ToTable("leasing_invoice_history"); b.HasKey(x => x.Id);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb"); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<LeasingInvoice>().WithMany().HasForeignKey(x => new { x.AccountId, x.InvoiceId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
