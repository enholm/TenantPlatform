using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Accounts;
namespace TenantPlatform.Infrastructure.Persistence.Configurations;
public sealed class LeasingLifecycleConfiguration:IEntityTypeConfiguration<LeasingLifecycle>
{
 public void Configure(EntityTypeBuilder<LeasingLifecycle> b)
 {
 b.ToTable("leasing_lifecycles");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Revision).IsConcurrencyToken();
b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
b.Ignore(x=>x.NoticeDate);b.HasIndex(x=>new{x.AccountId,x.AcquisitionId}).IsUnique();b.Property(x=>x.Notes).HasMaxLength(10000);
 }
}
public sealed class LeasingEquipmentConfiguration:IEntityTypeConfiguration<LeasingEquipment>
{
 public void Configure(EntityTypeBuilder<LeasingEquipment> b)
 {
 b.ToTable("leasing_equipment");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Revision).IsConcurrencyToken();
b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
b.HasOne<LeasingItem>().WithMany().HasForeignKey(x=>new{x.AccountId,x.ItemId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 b.Property(x=>x.InternalId).HasMaxLength(100);b.Property(x=>x.SerialNumber).HasMaxLength(200);b.Property(x=>x.Description).HasMaxLength(500);b.Property(x=>x.Notes).HasMaxLength(10000);
 b.HasIndex(x=>new{x.AccountId,x.InternalId}).IsUnique().HasFilter("\"InternalId\" IS NOT NULL");b.HasIndex(x=>new{x.AccountId,x.SerialNumber});b.HasIndex(x=>new{x.AccountId,x.AcquisitionId,x.Status});
 }
}
public sealed class LeasingLifecycleEventConfiguration:IEntityTypeConfiguration<LeasingLifecycleEvent>
{
 public void Configure(EntityTypeBuilder<LeasingLifecycleEvent> b)
 {
 b.ToTable("leasing_lifecycle_events");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Revision).IsConcurrencyToken();
b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.InputJson).HasColumnType("jsonb");b.Property(x=>x.BeforeJson).HasColumnType("jsonb");b.Property(x=>x.AfterJson).HasColumnType("jsonb");b.Property(x=>x.Reason).HasMaxLength(2000);b.Property(x=>x.Reference).HasMaxLength(500);b.HasIndex(x=>new{x.AccountId,x.AcquisitionId,x.RecordedUtc});
 }
}
public sealed class LeasingDispositionConfiguration:IEntityTypeConfiguration<LeasingDisposition>
{
 public void Configure(EntityTypeBuilder<LeasingDisposition> b)
 {
 b.ToTable("leasing_dispositions");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Quantity).HasPrecision(18,4);b.HasIndex(x=>new{x.AccountId,x.ItemId,x.Reversed});
 b.HasOne<LeasingItem>().WithMany().HasForeignKey(x=>new{x.AccountId,x.ItemId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 b.HasOne<LeasingLifecycleEvent>().WithMany().HasForeignKey(x=>new{x.AccountId,x.EventId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 b.HasOne<LeasingEquipment>().WithMany().HasForeignKey(x=>new{x.AccountId,x.EquipmentId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 }
}
public sealed class LeasingFollowupConfiguration:IEntityTypeConfiguration<LeasingFollowup>
{
 public void Configure(EntityTypeBuilder<LeasingFollowup> b)
 {
 b.ToTable("leasing_followups");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Revision).IsConcurrencyToken();
b.HasOne<LeasingAcquisition>().WithMany().HasForeignKey(x=>new{x.AccountId,x.AcquisitionId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
b.HasOne<LeasingFramework>().WithMany().HasForeignKey(x=>new{x.AccountId,x.FrameworkId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 b.Property(x=>x.Key).HasMaxLength(200);b.Property(x=>x.Comment).HasMaxLength(2000);b.HasIndex(x=>new{x.AccountId,x.Key}).IsUnique();b.HasIndex(x=>new{x.AccountId,x.Status,x.DueDate});
 }
}
public sealed class LeasingNotificationSettingsConfiguration:IEntityTypeConfiguration<LeasingNotificationSettings>
{
 public void Configure(EntityTypeBuilder<LeasingNotificationSettings> b)
 {
 b.ToTable("leasing_notification_settings");b.HasKey(x=>x.AccountId);b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.Property(x=>x.Revision).IsConcurrencyToken();
b.Property(x=>x.TimeZoneId).HasMaxLength(100);
 }
}
public sealed class LeasingNotificationConfiguration:IEntityTypeConfiguration<LeasingNotification>
{
 public void Configure(EntityTypeBuilder<LeasingNotification> b)
 {
 b.ToTable("leasing_notifications");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.AccountId,x.Id});b.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
b.HasOne<LeasingFollowup>().WithMany().HasForeignKey(x=>new{x.AccountId,x.FollowupId}).HasPrincipalKey(x=>new{x.AccountId,x.Id}).OnDelete(DeleteBehavior.Restrict);
 b.HasIndex(x=>new{x.AccountId,x.FollowupId,x.RecipientUserId,x.DaysBefore}).IsUnique();b.HasIndex(x=>new{x.AccountId,x.RecipientUserId,x.Status});b.Property(x=>x.ResultKey).HasMaxLength(100);
 }
}
