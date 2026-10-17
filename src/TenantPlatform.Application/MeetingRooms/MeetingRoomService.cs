using TenantPlatform.Application.Buildings;
using TenantPlatform.Core.MeetingRooms;
namespace TenantPlatform.Application.MeetingRooms;

public sealed class MeetingRoomService(MeetingRoomAccess access, IMeetingRoomData data) : IMeetingRoomService
{
    public async Task<List<MeetingRoomDto>> ListAsync(Guid accountId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var scope = await access.RequireAsync(accountId, false, cancellationToken);
        return await data.ReadRoomsAsync(accountId, MeetingRoomAccess.CanManage(scope), includeInactive, null, cancellationToken);
    }
    public async Task<MeetingRoomDto?> GetAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default)
    {
        var scope = await access.RequireAsync(accountId, false, cancellationToken);
        return (await data.ReadRoomsAsync(accountId, MeetingRoomAccess.CanManage(scope), true, roomId, cancellationToken)).SingleOrDefault();
    }
    public async Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(accountId, true, cancellationToken);
        return (await data.ReadConfigurationAsync(accountId, cancellationToken)).Buildings.OrderBy(x => x.Name).Select(x => new BuildingListItemDto { Id = x.Id, Name = x.Name }).ToList();
    }
    public Task<Guid> CreateAsync(Guid accountId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) => Save(accountId, null, request, cancellationToken);
    public async Task UpdateAsync(Guid accountId, Guid roomId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) => _ = await Save(accountId, roomId, request, cancellationToken);
    private async Task<Guid> Save(Guid account, Guid? id, SaveMeetingRoomRequest request, CancellationToken ct)
    {
        var input = new SaveMeetingRoomRequest { BuildingId = request.BuildingId, Name = request.Name, Description = request.Description, Capacity = request.Capacity,
            IsActive = request.IsActive, CalendarIntegrationId = request.CalendarIntegrationId, ExternalCalendarId = request.ExternalCalendarId, ExternalResourceEmail = request.ExternalResourceEmail };
        var actor = await access.RequireAsync(account, true, ct); MeetingRoomRules.Validate(input);
        await using var tx = await data.BeginConfigurationAsync(account, ct);
        await access.RevalidateAsync(actor, true, ct);
        if (!tx.State.Buildings.Any(x => x.Id == input.BuildingId)) throw new MeetingRoomValidationException("BuildingNotFound");
        if (input.CalendarIntegrationId.HasValue && !tx.State.Integrations.Any(x => x.Id == input.CalendarIntegrationId)) throw new MeetingRoomValidationException("CalendarIntegrationNotFound");
        var room = id.HasValue ? tx.State.Rooms.SingleOrDefault(x => x.Id == id) ?? throw new MeetingRoomValidationException("MeetingRoomNotFound")
            : new MeetingRoom { Id = Guid.NewGuid(), AccountId = account };
        room.BuildingId = input.BuildingId; room.Name = input.Name.Trim(); room.Description = input.Description?.Trim(); room.Capacity = input.Capacity; room.IsActive = input.IsActive;
        if (!id.HasValue) tx.State.Rooms.Add(room);
        var mapping = tx.State.Mappings.SingleOrDefault(x => x.MeetingRoomId == room.Id);
        if (!input.CalendarIntegrationId.HasValue) { if (mapping is not null) tx.State.Mappings.Remove(mapping); }
        else
        {
            var externalId = input.ExternalCalendarId!.Trim();
            if (tx.State.Mappings.Any(x => x.CalendarIntegrationId == input.CalendarIntegrationId && x.ExternalCalendarId == externalId && x.MeetingRoomId != room.Id))
                throw new MeetingRoomValidationException("MeetingRoomCalendarAlreadyMapped");
            if (mapping is null) { mapping = new MeetingRoomCalendar { Id = Guid.NewGuid(), AccountId = account, MeetingRoomId = room.Id }; tx.State.Mappings.Add(mapping); }
            mapping.CalendarIntegrationId = input.CalendarIntegrationId.Value; mapping.ExternalCalendarId = externalId; mapping.ExternalResourceEmail = input.ExternalResourceEmail?.Trim();
        }
        await tx.CommitAsync(ct); return room.Id;
    }
    public async Task DeactivateAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default)
    {
        var actor = await access.RequireAsync(accountId, true, cancellationToken);
        await using var tx = await data.BeginConfigurationAsync(accountId, cancellationToken);
        await access.RevalidateAsync(actor, true, cancellationToken);
        var room = tx.State.Rooms.SingleOrDefault(x => x.Id == roomId) ?? throw new MeetingRoomValidationException("MeetingRoomNotFound");
        room.IsActive = false; await tx.CommitAsync(cancellationToken);
    }
}
