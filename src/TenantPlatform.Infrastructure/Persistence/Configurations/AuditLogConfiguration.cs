using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Auditing;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration
    : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserEmail)
            .HasMaxLength(320);

        builder.Property(x => x.EntityType)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ChangesJson)
            .HasColumnType("jsonb");

        builder.HasIndex(x => x.TimestampUtc);
        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

