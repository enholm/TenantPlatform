namespace TenantPlatform.Web.Services.MeetingRooms;

public class SaveMeetingRoomRequest
{
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CalendarIntegrationId { get; set; }
    public string? ExternalCalendarId { get; set; }
    public string? ExternalResourceEmail { get; set; }
}

public class MeetingRoomDto : SaveMeetingRoomRequest
{
    public Guid Id { get; init; }
    public string BuildingName { get; init; } = string.Empty;
}
