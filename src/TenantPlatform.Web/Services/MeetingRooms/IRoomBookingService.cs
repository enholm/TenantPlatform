namespace TenantPlatform.Web.Services.MeetingRooms;

public interface IRoomBookingService
{
    Task<List<RoomBookingDto>> ListAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<RoomBookingDto?> GetAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid accountId, Guid roomId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid accountId, Guid bookingId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
}
