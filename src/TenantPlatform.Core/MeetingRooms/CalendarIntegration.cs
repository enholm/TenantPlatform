namespace TenantPlatform.Core.MeetingRooms;

public class CalendarIntegration
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CalendarProvider Provider { get; set; }
    public bool IsActive { get; set; } = true;
}
