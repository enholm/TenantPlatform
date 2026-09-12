using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.MeetingRooms;

public class RoomBookingService(
    IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization) : IRoomBookingService
{
    public async Task<List<RoomBookingDto>> ListAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        ValidateInterval(from, to);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MeetingRooms.AnyAsync(x => x.AccountId == accountId && x.Id == roomId, cancellationToken))
            throw new MeetingRoomValidationException("MeetingRoomNotFound");
        return await Query(db, accountId).Where(x => x.MeetingRoomId == roomId &&
                x.StartUtc < to.ToUniversalTime() && x.EndUtc > from.ToUniversalTime())
            .OrderBy(x => x.StartUtc).ToListAsync(cancellationToken);
    }

    public async Task<RoomBookingDto?> GetAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await Query(db, accountId).SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
    }

    private static IQueryable<RoomBookingDto> Query(TenantPlatformDbContext db, Guid accountId) =>
        from booking in db.RoomBookings.AsNoTracking()
        join user in db.Users.AsNoTracking() on booking.OrganizerUserId equals user.Id
        where booking.AccountId == accountId
        select new RoomBookingDto
        {
            Id = booking.Id, MeetingRoomId = booking.MeetingRoomId,
            OrganizerUserId = booking.OrganizerUserId, OrganizerName = user.FirstName + " " + user.LastName,
            Subject = booking.Subject, Description = booking.Description,
            StartUtc = booking.StartUtc, EndUtc = booking.EndUtc, Status = booking.Status, UpdatedUtc = booking.UpdatedUtc
        };

    public async Task<bool> IsAvailableAsync(Guid accountId, Guid roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        ValidateInterval(from, to);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await (from room in db.MeetingRooms
                            join building in db.Buildings on room.BuildingId equals building.Id
                            where room.AccountId == accountId && building.AccountId == accountId &&
                                room.Id == roomId && room.IsActive && building.IsActive
                            select room.Id).AnyAsync(cancellationToken);
        // Availability is advisory; create/update repeat this check under a room lock.
        return active && !await OverlapsAsync(db, accountId, roomId, from, to, null, cancellationToken);
    }

    public async Task<Guid> CreateAsync(Guid accountId, Guid roomId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        Validate(request);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var room = await LockRoomAsync(db, accountId, roomId, cancellationToken);
        await RequireActiveAsync(db, accountId, room, cancellationToken);
        if (await OverlapsAsync(db, accountId, roomId, request.StartUtc, request.EndUtc, null, cancellationToken))
            throw new MeetingRoomValidationException("RoomBookingOverlap");

        var now = DateTimeOffset.UtcNow;
        var booking = new RoomBooking
        {
            Id = Guid.NewGuid(), AccountId = accountId, MeetingRoomId = roomId,
            OrganizerUserId = userContext.Current.UserId, Subject = request.Subject.Trim(),
            Description = request.Description?.Trim(), StartUtc = request.StartUtc.ToUniversalTime(),
            EndUtc = request.EndUtc.ToUniversalTime(), CreatedUtc = now, UpdatedUtc = now
        };
        db.RoomBookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return booking.Id;
    }

    public async Task UpdateAsync(Guid accountId, Guid bookingId, SaveRoomBookingRequest request, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        Validate(request);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var roomId = await GetRoomIdAsync(db, accountId, bookingId, cancellationToken);
        var room = await LockRoomAsync(db, accountId, roomId, cancellationToken);
        var booking = await db.RoomBookings.SingleAsync(x => x.AccountId == accountId && x.Id == bookingId, cancellationToken);
        await RequireOwnerOrAdminAsync(booking, cancellationToken);
        if (booking.Status == RoomBookingStatus.Cancelled)
            throw new MeetingRoomValidationException("RoomBookingCancelled");
        await RequireActiveAsync(db, accountId, room, cancellationToken);
        if (await OverlapsAsync(db, accountId, roomId, request.StartUtc, request.EndUtc, booking.Id, cancellationToken))
            throw new MeetingRoomValidationException("RoomBookingOverlap");

        booking.Subject = request.Subject.Trim();
        booking.Description = request.Description?.Trim();
        booking.StartUtc = request.StartUtc.ToUniversalTime();
        booking.EndUtc = request.EndUtc.ToUniversalTime();
        booking.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var roomId = await GetRoomIdAsync(db, accountId, bookingId, cancellationToken);
        await LockRoomAsync(db, accountId, roomId, cancellationToken);
        var booking = await db.RoomBookings.SingleAsync(x => x.AccountId == accountId && x.Id == bookingId, cancellationToken);
        await RequireOwnerOrAdminAsync(booking, cancellationToken);
        if (booking.Status != RoomBookingStatus.Cancelled)
        {
            booking.Status = RoomBookingStatus.Cancelled;
            booking.UpdatedUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RequireOwnerOrAdminAsync(RoomBooking booking, CancellationToken cancellationToken)
    {
        if (booking.OrganizerUserId != userContext.Current.UserId &&
            !await authorization.CanManageMeetingRoomsAsync(cancellationToken))
            throw new UnauthorizedAccessException();
    }

    private static async Task<Guid> GetRoomIdAsync(TenantPlatformDbContext db, Guid accountId, Guid bookingId, CancellationToken cancellationToken) =>
        await db.RoomBookings.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == bookingId)
            .Select(x => (Guid?)x.MeetingRoomId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new MeetingRoomValidationException("RoomBookingNotFound");

    private static async Task<MeetingRoom> LockRoomAsync(TenantPlatformDbContext db, Guid accountId, Guid roomId, CancellationToken cancellationToken)
    {
        // All booking writers lock the same room before reading bookings. At READ
        // COMMITTED the subsequent overlap query sees the previous writer's commit.
        var rooms = await db.MeetingRooms.FromSqlInterpolated(
            $"SELECT * FROM meeting_rooms WHERE \"AccountId\" = {accountId} AND \"Id\" = {roomId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rooms.SingleOrDefault() ?? throw new MeetingRoomValidationException("MeetingRoomNotFound");
    }

    private static async Task RequireActiveAsync(TenantPlatformDbContext db, Guid accountId, MeetingRoom room, CancellationToken cancellationToken)
    {
        if (!room.IsActive || !await db.Buildings.AnyAsync(x =>
                x.AccountId == accountId && x.Id == room.BuildingId && x.IsActive, cancellationToken))
            throw new MeetingRoomValidationException("MeetingRoomInactive");
    }

    private static Task<bool> OverlapsAsync(TenantPlatformDbContext db, Guid accountId, Guid roomId,
        DateTimeOffset from, DateTimeOffset to, Guid? excludeId, CancellationToken cancellationToken) =>
        db.RoomBookings.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.MeetingRoomId == roomId &&
            x.Status == RoomBookingStatus.Confirmed && (!excludeId.HasValue || x.Id != excludeId.Value) &&
            x.StartUtc < to.ToUniversalTime() && x.EndUtc > from.ToUniversalTime(), cancellationToken);

    private static void Validate(SaveRoomBookingRequest request)
    {
        ValidateInterval(request.StartUtc, request.EndUtc);
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 200 || request.Description?.Length > 4000)
            throw new MeetingRoomValidationException("RoomBookingInvalidDetails");
    }

    private static void ValidateInterval(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) throw new MeetingRoomValidationException("RoomBookingInvalidInterval");
    }
}
