using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementDocumentConfiguration : IEntityTypeConfiguration<AgreementDocument>
{
    public void Configure(EntityTypeBuilder<AgreementDocument> b)
    {
        b.ToTable("agreement_documents", t =>
        {
            t.HasCheckConstraint("CK_agreement_documents_size", "\"Size\" > 0");
            t.HasCheckConstraint("CK_agreement_documents_category", "\"Category\" IN (1,2)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.AgreementId, x.Id });
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.MediaType).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.UploadedUtc });
    }
}
