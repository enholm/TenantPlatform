using TenantPlatform.Application.MeetingRooms;
using TenantPlatform.Application.Buildings;
namespace TenantPlatform.Web.Services.MeetingRooms;
public sealed class MeetingRoomService(TenantPlatform.Application.MeetingRooms.MeetingRoomService service) : IMeetingRoomService
{
    public Task<List<MeetingRoomDto>> ListAsync(Guid accountId, bool includeInactive = false, CancellationToken cancellationToken = default) => service.ListAsync(accountId, includeInactive, cancellationToken);
    public Task<MeetingRoomDto?> GetAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default) => service.GetAsync(accountId, roomId, cancellationToken);
    public Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetBuildingsAsync(accountId, cancellationToken);
    public Task<Guid> CreateAsync(Guid accountId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) => service.CreateAsync(accountId, request, cancellationToken);
    public Task UpdateAsync(Guid accountId, Guid roomId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) => service.UpdateAsync(accountId, roomId, request, cancellationToken);
    public Task DeactivateAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default) => service.DeactivateAsync(accountId, roomId, cancellationToken);
}
