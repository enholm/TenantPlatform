using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Web.Services.MeetingRooms;

public class SaveCalendarIntegrationRequest
{
    public string Name { get; set; } = string.Empty;
    public CalendarProvider Provider { get; set; } = CalendarProvider.Microsoft365;
    public bool IsActive { get; set; } = true;
}

public class CalendarIntegrationDto : SaveCalendarIntegrationRequest
{
    public Guid Id { get; init; }
}
