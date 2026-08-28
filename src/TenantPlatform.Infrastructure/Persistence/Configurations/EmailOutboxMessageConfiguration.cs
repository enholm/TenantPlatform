using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class EmailOutboxMessageConfiguration
    : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(
        EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ToAddress)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.ReplyToAddress)
            .HasMaxLength(320);

        builder.Property(x => x.Subject)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Body)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(x => x.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.ServiceRequestId);

        builder.HasIndex(x => new
        {
            x.Status,
            x.NextAttemptAt
        });
    }
}

