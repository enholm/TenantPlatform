using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class MeetingRoomCalendarConfiguration : IEntityTypeConfiguration<MeetingRoomCalendar>
{
    public void Configure(EntityTypeBuilder<MeetingRoomCalendar> builder)
    {
        builder.ToTable("meeting_room_calendars");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalCalendarId).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ExternalResourceEmail).HasMaxLength(320);
        builder.HasOne<MeetingRoom>().WithMany()
            .HasForeignKey(x => new { x.AccountId, x.MeetingRoomId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CalendarIntegration>().WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CalendarIntegrationId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AccountId, x.MeetingRoomId }).IsUnique();
        builder.HasIndex(x => new { x.AccountId, x.CalendarIntegrationId, x.ExternalCalendarId }).IsUnique();
    }
}
