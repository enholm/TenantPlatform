using TenantPlatform.Web.Services.Buildings;

namespace TenantPlatform.Web.Services.MeetingRooms;

public interface IMeetingRoomService
{
    Task<List<MeetingRoomDto>> ListAsync(Guid accountId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<MeetingRoomDto?> GetAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default);
    Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid accountId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid accountId, Guid roomId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default);
}
