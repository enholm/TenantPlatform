namespace TenantPlatform.Core.MeetingRooms;

public class MeetingRoom
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
}
