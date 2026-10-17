using TenantPlatform.Application.Security;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Core.Properties;
namespace TenantPlatform.Application.MeetingRooms;

public sealed record MeetingRoomState(List<MeetingRoom> Rooms, List<Building> Buildings, List<CalendarIntegration> Integrations, List<MeetingRoomCalendar> Mappings);
public sealed record BookingAvailability(MeetingRoom? Room, bool BuildingActive, IReadOnlyList<RoomBooking> Bookings);
public interface IMeetingRoomData
{
    Task<MeetingRoomState> ReadConfigurationAsync(Guid account, CancellationToken ct);
    Task<List<MeetingRoomDto>> ReadRoomsAsync(Guid account, bool administration, bool includeInactive, Guid? room, CancellationToken ct);
    Task<List<RoomBookingDto>> ReadBookingsAsync(Guid account, Guid room, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<RoomBookingDto?> ReadBookingAsync(Guid account, Guid booking, CancellationToken ct);
    Task<BookingAvailability> ReadAvailabilityAsync(Guid account, Guid room, CancellationToken ct);
    Task<IMeetingRoomTransaction> BeginConfigurationAsync(Guid account, CancellationToken ct);
    Task<IRoomBookingTransaction> BeginRoomAsync(Guid account, Guid room, CancellationToken ct);
    Task<IRoomBookingTransaction> BeginBookingAsync(Guid account, Guid booking, CancellationToken ct);
}
public interface IMeetingRoomTransaction : IAsyncDisposable
{
    MeetingRoomState State { get; }
    Task CommitAsync(CancellationToken ct);
}
public interface IRoomBookingTransaction : IAsyncDisposable
{
    MeetingRoom Room { get; }
    bool BuildingActive { get; }
    List<RoomBooking> Bookings { get; }
    Task CommitAsync(CancellationToken ct);
}
public sealed class MeetingRoomAccess(IAuthorizationService authorization)
{
    public static bool CanManage(AccountActorScope scope) => scope.Assignments.Any(x => x.BuildingId is null && x.OrganizationId is null &&
        LegacyRolePermissions.Grants(x, Permissions.MeetingRooms.Manage));
    public async Task<AccountActorScope> RequireAsync(Guid account, bool administration, CancellationToken ct)
    {
        var scope = await authorization.GetAccountActorScopeAsync(account, ct);
        if (administration && !CanManage(scope)) throw new UnauthorizedAccessException();
        return scope;
    }
    public async Task<AccountActorScope> RevalidateAsync(AccountActorScope expected, bool administration, CancellationToken ct)
    {
        var current = await RequireAsync(expected.AccountId, administration, ct);
        if (current.UserId != expected.UserId) throw new UnauthorizedAccessException();
        return current;
    }
}
