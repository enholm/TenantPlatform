using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementIndexConfiguration : IEntityTypeConfiguration<AgreementIndex>
{
    public void Configure(EntityTypeBuilder<AgreementIndex> b)
    {
        b.ToTable("agreement_indices"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
        b.Property(x => x.Code).HasMaxLength(50); b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.Source).HasMaxLength(2000);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementIndexValueConfiguration : IEntityTypeConfiguration<AgreementIndexValue>
{
    public void Configure(EntityTypeBuilder<AgreementIndexValue> b)
    {
        b.ToTable("agreement_index_values", t => t.HasCheckConstraint("CK_index_value_positive", "\"Value\" > 0")); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.AccountId, x.IndexId, x.Period, x.Revision }).IsUnique();
        b.Property(x => x.Value).HasPrecision(24, 8); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<AgreementIndex>().WithMany().HasForeignKey(x => new { x.AccountId, x.IndexId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementAdjustmentRuleConfiguration : IEntityTypeConfiguration<AgreementAdjustmentRule>
{
    public void Configure(EntityTypeBuilder<AgreementAdjustmentRule> b)
    {
        b.ToTable("agreement_adjustment_rules"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.LineId, x.EffectiveFrom }).IsUnique(); b.Property(x => x.Reason).HasMaxLength(2000);
        foreach (var name in new[] { "SharePercent", "AdditionPercent", "FixedPercent", "FloorPercent", "CeilingPercent" }) b.Property(name).HasPrecision(18, 6);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementIndex>().WithMany().HasForeignKey(x => new { x.AccountId, x.IndexId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementPriceVersion>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.BasePriceVersionId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementAdjustmentDocumentConfiguration : IEntityTypeConfiguration<AgreementAdjustmentDocument>
{
    public void Configure(EntityTypeBuilder<AgreementAdjustmentDocument> b)
    {
        b.ToTable("agreement_adjustment_documents"); b.HasKey(x => new { x.RuleId, x.DocumentId });
        b.HasOne<AgreementAdjustmentRule>().WithMany(x => x.Documents).HasForeignKey(x => new { x.AccountId, x.AgreementId, x.RuleId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementDocument>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.DocumentId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementAdjustmentProposalConfiguration : IEntityTypeConfiguration<AgreementAdjustmentProposal>
{
    public void Configure(EntityTypeBuilder<AgreementAdjustmentProposal> b)
    {
        b.ToTable("agreement_adjustment_proposals"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.LineId, x.ScheduledDate }).IsUnique().HasFilter("\"Status\" = 1");
        b.Property(x => x.OldPrice).HasPrecision(18, 4); b.Property(x => x.NewPrice).HasPrecision(18, 4); b.Property(x => x.ChosenPercent).HasPrecision(18, 6);
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.CalculationJson).HasColumnType("jsonb"); b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementAdjustmentRule>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.RuleId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementBasisConfiguration : IEntityTypeConfiguration<AgreementBasis>
{
    public void Configure(EntityTypeBuilder<AgreementBasis> b)
    {
        b.ToTable("agreement_bases", t => t.HasCheckConstraint("CK_basis_direction", "\"Direction\" IN (1,2)")); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.OriginalBasisId }).IsUnique().HasFilter("\"OriginalBasisId\" IS NOT NULL AND \"Status\" = 0");
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId }).HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementBasis>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.OriginalBasisId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GeneratedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementBasisSnapshotConfiguration : IEntityTypeConfiguration<AgreementBasisSnapshot>
{
    public void Configure(EntityTypeBuilder<AgreementBasisSnapshot> b)
    {
        b.ToTable("agreement_basis_snapshots"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.AccountId, x.BasisId, x.Revision }).IsUnique();
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.DataJson).HasColumnType("jsonb"); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<AgreementBasis>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.BasisId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementBasisEventConfiguration : IEntityTypeConfiguration<AgreementBasisEvent>
{
    public void Configure(EntityTypeBuilder<AgreementBasisEvent> b)
    {
        b.ToTable("agreement_basis_events"); b.HasKey(x => x.Id); b.Property(x => x.EventKey).HasMaxLength(100);
        b.HasIndex(x => new { x.AccountId, x.LineId, x.EventKey }).IsUnique().HasFilter("\"Active\"");
        b.HasOne<AgreementBasis>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.BasisId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineId }).HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
