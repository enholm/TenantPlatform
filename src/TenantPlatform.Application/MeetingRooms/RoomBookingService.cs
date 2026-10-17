using TenantPlatform.Core.MeetingRooms;
namespace TenantPlatform.Application.MeetingRooms;

public sealed class RoomBookingService(MeetingRoomAccess access, IMeetingRoomData data, TimeProvider clock) : IRoomBookingService
{
    public async Task<List<RoomBookingDto>> ListAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(accountId, false, cancellationToken);
        RoomBookingRules.ValidateInterval(from, to);
        return await data.ReadBookingsAsync(accountId, roomId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken);
    }
    public async Task<RoomBookingDto?> GetAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(accountId, false, cancellationToken);
        return await data.ReadBookingAsync(accountId, bookingId, cancellationToken);
    }
    private static bool Overlaps(IEnumerable<RoomBooking> bookings, DateTimeOffset from, DateTimeOffset to, Guid? exclude) =>
        bookings.Any(x => x.Id != exclude && x.Status == RoomBookingStatus.Confirmed && x.StartUtc < to && x.EndUtc > from);
    public async Task<bool> IsAvailableAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(accountId, false, cancellationToken);
        RoomBookingRules.ValidateInterval(from, to);
        var state = await data.ReadAvailabilityAsync(accountId, roomId, cancellationToken);
        return state.Room is { IsActive: true } && state.BuildingActive && !Overlaps(state.Bookings, from, to, null);
    }
    private static SaveRoomBookingRequest Snapshot(SaveRoomBookingRequest input) => new()
    { Subject = input.Subject, Description = input.Description, StartUtc = input.StartUtc.ToUniversalTime(), EndUtc = input.EndUtc.ToUniversalTime() };
    public async Task<Guid> CreateAsync(Guid accountId, Guid roomId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default)
    {
        var input = Snapshot(request);
        var actor = await access.RequireAsync(accountId, false, cancellationToken);
        RoomBookingRules.Validate(input);
        await using var tx = await data.BeginRoomAsync(accountId, roomId, cancellationToken);
        await access.RevalidateAsync(actor, false, cancellationToken);
        if (!tx.Room.IsActive || !tx.BuildingActive) throw new MeetingRoomValidationException("MeetingRoomInactive");
        if (Overlaps(tx.Bookings, input.StartUtc, input.EndUtc, null)) throw new MeetingRoomValidationException("RoomBookingOverlap");
        var now = clock.GetUtcNow();
        var booking = new RoomBooking { Id = Guid.NewGuid(), AccountId = accountId, MeetingRoomId = roomId, OrganizerUserId = actor.UserId,
            Subject = input.Subject.Trim(), Description = input.Description?.Trim(), StartUtc = input.StartUtc, EndUtc = input.EndUtc, CreatedUtc = now, UpdatedUtc = now };
        tx.Bookings.Add(booking); await tx.CommitAsync(cancellationToken); return booking.Id;
    }
    public async Task UpdateAsync(Guid accountId, Guid bookingId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default)
    {
        var input = Snapshot(request);
        var actor = await access.RequireAsync(accountId, false, cancellationToken); RoomBookingRules.Validate(input);
        await using var tx = await data.BeginBookingAsync(accountId, bookingId, cancellationToken);
        var current = await access.RevalidateAsync(actor, false, cancellationToken);
        var booking = tx.Bookings.SingleOrDefault(x => x.Id == bookingId) ?? throw new MeetingRoomValidationException("RoomBookingNotFound");
        if (booking.OrganizerUserId != current.UserId && !MeetingRoomAccess.CanManage(current)) throw new UnauthorizedAccessException();
        if (booking.Status == RoomBookingStatus.Cancelled) throw new MeetingRoomValidationException("RoomBookingCancelled");
        if (!tx.Room.IsActive || !tx.BuildingActive) throw new MeetingRoomValidationException("MeetingRoomInactive");
        if (Overlaps(tx.Bookings, input.StartUtc, input.EndUtc, bookingId)) throw new MeetingRoomValidationException("RoomBookingOverlap");
        var now = clock.GetUtcNow();
        booking.Subject = input.Subject.Trim(); booking.Description = input.Description?.Trim(); booking.StartUtc = input.StartUtc; booking.EndUtc = input.EndUtc; booking.UpdatedUtc = now;
        await tx.CommitAsync(cancellationToken);
    }
    public async Task CancelAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        var actor = await access.RequireAsync(accountId, false, cancellationToken);
        await using var tx = await data.BeginBookingAsync(accountId, bookingId, cancellationToken);
        var current = await access.RevalidateAsync(actor, false, cancellationToken);
        var booking = tx.Bookings.SingleOrDefault(x => x.Id == bookingId) ?? throw new MeetingRoomValidationException("RoomBookingNotFound");
        if (booking.OrganizerUserId != current.UserId && !MeetingRoomAccess.CanManage(current)) throw new UnauthorizedAccessException();
        if (booking.Status == RoomBookingStatus.Cancelled) return;
        booking.Status = RoomBookingStatus.Cancelled; booking.UpdatedUtc = clock.GetUtcNow();
        await tx.CommitAsync(cancellationToken);
    }
}
