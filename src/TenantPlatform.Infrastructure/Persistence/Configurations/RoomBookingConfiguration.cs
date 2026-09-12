using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class RoomBookingConfiguration : IEntityTypeConfiguration<RoomBooking>
{
    public void Configure(EntityTypeBuilder<RoomBooking> builder)
    {
        builder.ToTable("room_bookings", table =>
        {
            table.HasCheckConstraint("CK_room_bookings_interval", "\"EndUtc\" > \"StartUtc\"");
            table.HasCheckConstraint("CK_room_bookings_status", "\"Status\" IN (1, 2)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.HasOne<MeetingRoom>().WithMany()
            .HasForeignKey(x => new { x.AccountId, x.MeetingRoomId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OrganizerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AccountId, x.MeetingRoomId, x.StartUtc, x.EndUtc });
    }
}
