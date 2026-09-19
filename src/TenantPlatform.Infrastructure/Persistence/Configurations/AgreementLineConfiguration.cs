using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementLineConfiguration : IEntityTypeConfiguration<AgreementLine>
{
    public void Configure(EntityTypeBuilder<AgreementLine> b)
    {
        b.ToTable("agreement_lines"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementDeliveryGroupConfiguration : IEntityTypeConfiguration<AgreementDeliveryGroup>
{
    public void Configure(EntityTypeBuilder<AgreementDeliveryGroup> b)
    {
        b.ToTable("agreement_delivery_groups"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementLineVersionConfiguration : IEntityTypeConfiguration<AgreementLineVersion>
{
    public void Configure(EntityTypeBuilder<AgreementLineVersion> b)
    {
        b.ToTable("agreement_line_versions"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.HasIndex(x => new { x.AccountId, x.LineId, x.Sequence }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.PayableSourceLineId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementDeliveryGroup>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.DeliveryGroupId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementPriceVersionConfiguration : IEntityTypeConfiguration<AgreementPriceVersion>
{
    public void Configure(EntityTypeBuilder<AgreementPriceVersion> b)
    {
        b.ToTable("agreement_price_versions", t => t.HasCheckConstraint("CK_agreement_price_positive", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0"));
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.AccountId, x.LineId, x.Sequence }).IsUnique();
        b.Property(x => x.Quantity).HasPrecision(18, 4); b.Property(x => x.UnitPrice).HasPrecision(18, 4);
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<AgreementLine>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AgreementLineDocumentConfiguration : IEntityTypeConfiguration<AgreementLineDocument>
{
    public void Configure(EntityTypeBuilder<AgreementLineDocument> b)
    {
        b.ToTable("agreement_line_documents"); b.HasKey(x => new { x.LineVersionId, x.DocumentId });
        b.HasOne<AgreementLineVersion>().WithMany(x => x.Documents).HasForeignKey(x => new { x.AccountId, x.AgreementId, x.LineVersionId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgreementDocument>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId, x.DocumentId })
            .HasPrincipalKey(x => new { x.AccountId, x.AgreementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
