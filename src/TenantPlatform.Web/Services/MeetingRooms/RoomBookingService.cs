using TenantPlatform.Application.MeetingRooms;
using TenantPlatform.Application.Buildings;
namespace TenantPlatform.Web.Services.MeetingRooms;
public sealed class RoomBookingService(TenantPlatform.Application.MeetingRooms.RoomBookingService service) : IRoomBookingService
{
    public Task<List<RoomBookingDto>> ListAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => service.ListAsync(accountId, roomId, from, to, cancellationToken);
    public Task<RoomBookingDto?> GetAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default) => service.GetAsync(accountId, bookingId, cancellationToken);
    public Task<bool> IsAvailableAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => service.IsAvailableAsync(accountId, roomId, from, to, cancellationToken);
    public Task<Guid> CreateAsync(Guid accountId, Guid roomId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default) => service.CreateAsync(accountId, roomId, request, cancellationToken);
    public Task UpdateAsync(Guid accountId, Guid bookingId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default) => service.UpdateAsync(accountId, bookingId, request, cancellationToken);
    public Task CancelAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default) => service.CancelAsync(accountId, bookingId, cancellationToken);
}
