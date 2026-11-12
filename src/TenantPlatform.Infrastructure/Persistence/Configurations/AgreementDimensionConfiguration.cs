using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class AgreementDimensionRuleConfiguration : IEntityTypeConfiguration<AgreementDimensionRule>
{
    public void Configure(EntityTypeBuilder<AgreementDimensionRule> b)
    {
        b.ToTable("agreement_dimension_rules", t => t.HasCheckConstraint("CK_agreement_dimension_required_enabled", "NOT \"IsRequired\" OR \"IsEnabled\""));
        b.HasKey(x => new { x.AccountId, x.DimensionId });
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<Dimension>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AgreementDimensionAssignmentConfiguration : IEntityTypeConfiguration<AgreementDimensionAssignment>
{
    public void Configure(EntityTypeBuilder<AgreementDimensionAssignment> b)
    {
        b.ToTable("agreement_dimension_assignments");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.DimensionId, x.ValueId }).IsUnique();
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DimensionValue>().WithMany().HasForeignKey(x => new { x.AccountId, x.DimensionId, x.ValueId })
            .HasPrincipalKey(x => new { x.AccountId, x.DimensionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AgreementDimensionHistoryConfiguration : IEntityTypeConfiguration<AgreementDimensionHistory>
{
    public void Configure(EntityTypeBuilder<AgreementDimensionHistory> b)
    {
        b.ToTable("agreement_dimension_history");
        b.HasKey(x => x.Id);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.RecordedUtc });
    }
}
