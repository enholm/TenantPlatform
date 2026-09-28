using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class LeasingOrderConfiguration : IEntityTypeConfiguration<LeasingOrder>
{
    public void Configure(EntityTypeBuilder<LeasingOrder> b)
    {
        b.ToTable("leasing_orders"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.Number }).IsUnique();
        b.Property(x => x.Number).HasMaxLength(100); b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Notes).HasMaxLength(10000); b.Property(x => x.ProposalJson).HasColumnType("jsonb");
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x => new { x.AccountId, x.FrameworkId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingOrderLineConfiguration : IEntityTypeConfiguration<LeasingOrderLine>
{
    public void Configure(EntityTypeBuilder<LeasingOrderLine> b)
    {
        b.ToTable("leasing_order_lines", t => t.HasCheckConstraint("CK_order_scope", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Fulfilled\" >= 0 AND \"Unreserved\" >= 0 AND \"Fulfilled\" + \"Unreserved\" <= CASE WHEN \"Method\" = 1 THEN \"Quantity\" ELSE 1 END"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id }); b.Ignore(x => x.Scope);
        foreach (var p in new[] { "Quantity", "UnitPrice", "VatPercent", "Fulfilled", "Unreserved" }) b.Property<decimal>(p).HasPrecision(20,4);
        b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.Unit).HasMaxLength(30); b.Property(x => x.ItemNumber).HasMaxLength(100);
        b.Property(x => x.ClassificationJson).HasColumnType("jsonb");
        b.HasOne<LeasingOrder>().WithMany(x => x.Lines).HasForeignKey(x => new { x.AccountId, x.OrderId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingOrderRealizationConfiguration : IEntityTypeConfiguration<LeasingOrderRealization>
{
    public void Configure(EntityTypeBuilder<LeasingOrderRealization> b)
    {
        b.ToTable("leasing_order_realizations"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.AccountId, x.ItemId }).IsUnique();
        b.Property(x => x.Scope).HasPrecision(20,4);
        foreach (var p in new[] { "ApprovedNet", "ApprovedVat", "ActualNet", "ActualVat" }) b.Property<decimal>(p).HasPrecision(20,2);
        b.HasOne<LeasingOrder>().WithMany().HasForeignKey(x => new { x.AccountId, x.OrderId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingOrderLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.OrderLineId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x => new { x.AccountId, x.AcquisitionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingItem>().WithMany().HasForeignKey(x => new { x.AccountId, x.ItemId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingOrderEventConfiguration : IEntityTypeConfiguration<LeasingOrderEvent>
{
    public void Configure(EntityTypeBuilder<LeasingOrderEvent> b)
    {
        b.ToTable("leasing_order_events"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(50); b.Property(x => x.Reason).HasMaxLength(2000);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasOne<LeasingOrder>().WithMany().HasForeignKey(x => new { x.AccountId, x.OrderId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingLimitChangeConfiguration : IEntityTypeConfiguration<LeasingLimitChange>
{
    public void Configure(EntityTypeBuilder<LeasingLimitChange> b)
    {
        b.ToTable("leasing_limit_changes"); b.HasKey(x => x.Id);
        b.Property(x => x.PreviousLimit).HasPrecision(20,2); b.Property(x => x.NewLimit).HasPrecision(20,2);
        b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.DocumentReference).HasMaxLength(500);
        b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x => new { x.AccountId, x.FrameworkId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
