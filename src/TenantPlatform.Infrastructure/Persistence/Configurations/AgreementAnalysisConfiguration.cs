using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementAnalysisConfiguration : IEntityTypeConfiguration<AgreementAnalysis>
{
    public void Configure(EntityTypeBuilder<AgreementAnalysis> b)
    {
        b.ToTable("agreement_analyses");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.Property(x => x.OriginalResultJson).HasColumnType("jsonb");
        b.Property(x => x.Model).HasMaxLength(200);
        b.Property(x => x.SchemaVersion).HasMaxLength(80);
        b.Property(x => x.ApprovedCounterpartyName).HasMaxLength(200);
        b.Property(x => x.ApprovedOrganizationNumber).HasMaxLength(50);
        b.Property(x => x.ApprovedAddress).HasMaxLength(1000);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.State, x.ExpiresUtc });
    }
}
public class AgreementAnalysisFileConfiguration : IEntityTypeConfiguration<AgreementAnalysisFile>
{
    public void Configure(EntityTypeBuilder<AgreementAnalysisFile> b)
    {
        b.ToTable("agreement_analysis_files");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.StorageKey).HasMaxLength(80);
        b.Property(x => x.MediaType).HasMaxLength(150);
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasOne<AgreementAnalysis>().WithMany(x => x.Files).HasForeignKey(x => new { x.AccountId, x.AnalysisId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AgreementDocument>().WithMany().HasForeignKey(x => x.AgreementDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementFindingConfiguration : IEntityTypeConfiguration<AgreementFinding>
{
    public void Configure(EntityTypeBuilder<AgreementFinding> b)
    {
        b.ToTable("agreement_findings");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.Property(x => x.OriginalValue).HasMaxLength(10000);
        b.Property(x => x.AdjustedValue).HasMaxLength(10000);
        b.Property(x => x.Parties).HasMaxLength(2000);
        b.Property(x => x.OriginalParties).HasMaxLength(2000);
        b.Property(x => x.Explanation).HasMaxLength(10000);
        b.HasOne<AgreementAnalysis>().WithMany(x => x.Findings).HasForeignKey(x => new { x.AccountId, x.AnalysisId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.AnalysisId, x.Position }).IsUnique();
    }
}
public class AgreementFindingSourceConfiguration : IEntityTypeConfiguration<AgreementFindingSource>
{
    public void Configure(EntityTypeBuilder<AgreementFindingSource> b)
    {
        b.ToTable("agreement_finding_sources");
        b.HasKey(x => x.Id);
        b.Property(x => x.Quote).HasMaxLength(20000);
        b.Property(x => x.Section).HasMaxLength(2000);
        b.HasOne<AgreementFinding>().WithMany(x => x.Sources).HasForeignKey(x => new { x.AccountId, x.AgreementId, x.FindingId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AgreementDocument>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.DocumentId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementAiUsageConfiguration : IEntityTypeConfiguration<AgreementAiUsage>
{
    public void Configure(EntityTypeBuilder<AgreementAiUsage> b)
    {
        b.ToTable("agreement_ai_usage");
        b.HasKey(x => x.Id);
        b.Property(x => x.Model).HasMaxLength(200);
        b.Property(x => x.ResponseId).HasMaxLength(200);
        b.Property(x => x.Status).HasMaxLength(80);
        b.HasIndex(x => new { x.AccountId, x.AnalysisId, x.CreatedUtc });
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
