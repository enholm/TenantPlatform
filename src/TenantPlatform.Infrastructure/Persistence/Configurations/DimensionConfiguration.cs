using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class DimensionConfiguration : IEntityTypeConfiguration<Dimension>
{
    public void Configure(EntityTypeBuilder<Dimension> b)
    {
        b.ToTable("dimensions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Code).HasMaxLength(50); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DimensionValueConfiguration : IEntityTypeConfiguration<DimensionValue>
{
    public void Configure(EntityTypeBuilder<DimensionValue> b)
    {
        b.ToTable("dimension_values"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.DimensionId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.DimensionId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Code).HasMaxLength(50); b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Dimension>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DimensionValue>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId, x.ParentId })
            .HasPrincipalKey(x => new { x.AccountId, x.DimensionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DimensionHistoryConfiguration : IEntityTypeConfiguration<DimensionHistory>
{
    public void Configure(EntityTypeBuilder<DimensionHistory> b)
    {
        b.ToTable("dimension_history"); b.HasKey(x => x.Id);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasOne<Dimension>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DimensionValue>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId, x.ValueId }).HasPrincipalKey(x => new { x.AccountId, x.DimensionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingDimensionRuleConfiguration : IEntityTypeConfiguration<LeasingDimensionRule>
{
    public void Configure(EntityTypeBuilder<LeasingDimensionRule> b)
    {
        b.ToTable("leasing_dimension_rules"); b.HasKey(x => new { x.AccountId, x.DimensionId }); b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Dimension>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingAllocationDimensionConfiguration : IEntityTypeConfiguration<LeasingAllocationDimension>
{
    public void Configure(EntityTypeBuilder<LeasingAllocationDimension> b)
    {
        b.ToTable("leasing_allocation_dimensions"); b.HasKey(x => new { x.AccountId, x.ItemId, x.DimensionId });
        b.Property(x => x.DimensionName).HasMaxLength(200); b.Property(x => x.DimensionCode).HasMaxLength(50);
        b.HasOne<LeasingItem>().WithMany(x => x.AllocationDimensions).HasForeignKey(x => new { x.AccountId, x.ItemId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Dimension>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingAllocationRowConfiguration : IEntityTypeConfiguration<LeasingAllocationRow>
{
    public void Configure(EntityTypeBuilder<LeasingAllocationRow> b)
    {
        b.ToTable("leasing_allocation_rows", t => t.HasCheckConstraint("CK_leasing_allocation_nonnegative", "\"InputValue\" >= 0"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.ItemId, x.Id }); b.Property(x => x.InputValue).HasPrecision(24, 4);
        b.HasOne<LeasingItem>().WithMany(x => x.AllocationRows).HasForeignKey(x => new { x.AccountId, x.ItemId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class LeasingDimensionSelectionConfiguration : IEntityTypeConfiguration<LeasingDimensionSelection>
{
    public void Configure(EntityTypeBuilder<LeasingDimensionSelection> b)
    {
        b.ToTable("leasing_dimension_selections"); b.HasKey(x => x.Id);
        b.Property(x => x.DimensionName).HasMaxLength(200); b.Property(x => x.DimensionCode).HasMaxLength(50);
        b.Property(x => x.ValueName).HasMaxLength(200); b.Property(x => x.ValueCode).HasMaxLength(50);
        // Paths use PostgreSQL text: no artificial hierarchy-depth limit.
        b.HasOne<LeasingItem>().WithMany(x => x.DimensionSelections).HasForeignKey(x => new { x.AccountId, x.ItemId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DimensionValue>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId, x.ValueId }).HasPrincipalKey(x => new { x.AccountId, x.DimensionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeasingAllocationRow>().WithMany().HasForeignKey(x => new { x.AccountId, x.ItemId, x.AllocationRowId }).HasPrincipalKey(x => new { x.AccountId, x.ItemId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
