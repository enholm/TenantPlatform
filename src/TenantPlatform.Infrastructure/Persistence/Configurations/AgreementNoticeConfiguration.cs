using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementNoticeHistoryConfiguration : IEntityTypeConfiguration<AgreementNoticeHistory>
{
    public void Configure(EntityTypeBuilder<AgreementNoticeHistory> b)
    {
        b.ToTable("agreement_notice_history");
        b.HasKey(x => x.Id);
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.CreatedUtc });
    }
}
