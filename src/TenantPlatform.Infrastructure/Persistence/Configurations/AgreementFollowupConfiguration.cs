using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class AgreementDeadlineConfiguration : IEntityTypeConfiguration<AgreementDeadline>
{
    public void Configure(EntityTypeBuilder<AgreementDeadline> b)
    {
        b.ToTable("agreement_deadlines");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.AccountId, x.Id });
        b.HasOne<Agreement>().WithMany().HasForeignKey(x => new { x.AccountId, x.AgreementId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.AgreementId, x.State });
        b.HasIndex(x => new { x.AccountId, x.State, x.Status, x.DueDate });
    }
}
public class AgreementDeadlineHistoryConfiguration : IEntityTypeConfiguration<AgreementDeadlineHistory>
{
    public void Configure(EntityTypeBuilder<AgreementDeadlineHistory> b)
    {
        b.ToTable("agreement_deadline_history");
        b.HasKey(x => x.Id);
        b.HasOne<AgreementDeadline>().WithMany().HasForeignKey(x => new { x.AccountId, x.DeadlineId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasIndex(x => new { x.AccountId, x.DeadlineId, x.CreatedUtc });
    }
}
public class AgreementReminderSettingsConfiguration : IEntityTypeConfiguration<AgreementReminderSettings>
{
    public void Configure(EntityTypeBuilder<AgreementReminderSettings> b)
    {
        b.ToTable("agreement_reminder_settings");
        b.HasKey(x => x.AccountId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.TimeZoneId).HasMaxLength(100);
        b.Property(x => x.Revision).IsConcurrencyToken();
    }
}
public class AgreementReminderConfiguration : IEntityTypeConfiguration<AgreementReminder>
{
    public void Configure(EntityTypeBuilder<AgreementReminder> b)
    {
        b.ToTable("agreement_reminders", t => t.HasCheckConstraint("CK_agreement_reminder_days", "\"DaysBefore\" >= 0"));
        b.HasKey(x => x.Id);
        b.HasOne<AgreementDeadline>().WithMany().HasForeignKey(x => new { x.AccountId, x.DeadlineId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.DeadlineId, x.DaysBefore }).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.Status, x.NextAttemptUtc });
        b.Property(x => x.RecipientAddress).HasMaxLength(320);
        b.Property(x => x.TransportId).HasMaxLength(200);
        b.Property(x => x.ResultKey).HasMaxLength(100);
        b.Property(x => x.ReservationId).IsConcurrencyToken();
        b.Property(x => x.Status).IsConcurrencyToken();
    }
}
