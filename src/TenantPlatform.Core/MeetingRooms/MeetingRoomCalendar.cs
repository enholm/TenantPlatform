namespace TenantPlatform.Core.MeetingRooms;

public class MeetingRoomCalendar
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid MeetingRoomId { get; set; }
    public Guid CalendarIntegrationId { get; set; }
    public string ExternalCalendarId { get; set; } = string.Empty;
    public string? ExternalResourceEmail { get; set; }
}
