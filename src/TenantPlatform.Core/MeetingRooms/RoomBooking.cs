namespace TenantPlatform.Core.MeetingRooms;

public class RoomBooking
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid MeetingRoomId { get; set; }
    public Guid OrganizerUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public RoomBookingStatus Status { get; set; } = RoomBookingStatus.Confirmed;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
