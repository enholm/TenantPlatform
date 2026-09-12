using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Web.Services.MeetingRooms;

public class SaveRoomBookingRequest
{
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
}

public class RoomBookingDto : SaveRoomBookingRequest
{
    public Guid Id { get; init; }
    public Guid MeetingRoomId { get; init; }
    public Guid OrganizerUserId { get; init; }
    public string OrganizerName { get; init; } = string.Empty;
    public RoomBookingStatus Status { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
