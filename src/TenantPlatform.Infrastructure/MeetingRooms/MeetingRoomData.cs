using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TenantPlatform.Application.MeetingRooms;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.MeetingRooms;

public sealed class MeetingRoomData(IDbContextFactory<TenantPlatformDbContext> factory) : IMeetingRoomData
{
    private static IQueryable<MeetingRoomDto> Query(TenantPlatformDbContext db, Guid accountId, bool administration, bool includeInactive) =>
        from room in db.MeetingRooms.AsNoTracking()
        join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
        join mapping in db.MeetingRoomCalendars.AsNoTracking().Where(x => x.AccountId == accountId)
            on room.Id equals mapping.MeetingRoomId into mappings
        from calendar in mappings.DefaultIfEmpty()
        where room.AccountId == accountId && building.AccountId == accountId &&
            (includeInactive || (room.IsActive && building.IsActive))
        select new MeetingRoomDto
        {
            Id = room.Id, BuildingId = room.BuildingId, BuildingName = building.Name,
            Name = room.Name, Description = room.Description, Capacity = room.Capacity, IsActive = room.IsActive,
            CalendarIntegrationId = administration && calendar != null ? calendar.CalendarIntegrationId : null,
            ExternalCalendarId = administration && calendar != null ? calendar.ExternalCalendarId : null,
            ExternalResourceEmail = administration && calendar != null ? calendar.ExternalResourceEmail : null
        };

    private static IQueryable<RoomBookingDto> BookingQuery(TenantPlatformDbContext db, Guid accountId) =>
        from booking in db.RoomBookings.AsNoTracking()
        join userRow in db.Users.AsNoTracking().Where(x => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == x.Id)) on booking.OrganizerUserId equals userRow.Id into users
        from user in users.DefaultIfEmpty()
        where booking.AccountId == accountId
        select new RoomBookingDto
        {
            Id = booking.Id, MeetingRoomId = booking.MeetingRoomId,
            OrganizerUserId = booking.OrganizerUserId, OrganizerName = user == null ? "" : user.FirstName + " " + user.LastName,
            Subject = booking.Subject, Description = booking.Description,
            StartUtc = booking.StartUtc, EndUtc = booking.EndUtc, Status = booking.Status, UpdatedUtc = booking.UpdatedUtc
        };


    public async Task<List<MeetingRoomDto>> ReadRoomsAsync(Guid account, bool administration, bool includeInactive, Guid? room, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Query(db, account, administration, includeInactive).Where(x => !room.HasValue || x.Id == room).OrderBy(x => x.BuildingName).ThenBy(x => x.Name).ToListAsync(ct);
    }
    public async Task<List<RoomBookingDto>> ReadBookingsAsync(Guid account, Guid room, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.MeetingRooms.AnyAsync(x => x.AccountId == account && x.Id == room, ct)) throw new MeetingRoomValidationException("MeetingRoomNotFound");
        return await BookingQuery(db, account).Where(x => x.MeetingRoomId == room && x.StartUtc < to && x.EndUtc > from).OrderBy(x => x.StartUtc).ToListAsync(ct);
    }
    public async Task<RoomBookingDto?> ReadBookingAsync(Guid account, Guid booking, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await BookingQuery(db, account).SingleOrDefaultAsync(x => x.Id == booking, ct);
    }
    public async Task<BookingAvailability> ReadAvailabilityAsync(Guid account, Guid roomId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var room = await db.MeetingRooms.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == roomId, ct);
        if (room is null) return new(null, false, []);
        var active = await db.Buildings.AnyAsync(x => x.AccountId == account && x.Id == room.BuildingId && x.IsActive, ct);
        var bookings = await db.RoomBookings.AsNoTracking().Where(x => x.AccountId == account && x.MeetingRoomId == roomId).ToListAsync(ct);
        return new(room, active, bookings);
    }
    private static async Task<MeetingRoomState> Load(TenantPlatformDbContext db, Guid account, CancellationToken ct) => new(
        await db.MeetingRooms.Where(x => x.AccountId == account).ToListAsync(ct), await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
        await db.CalendarIntegrations.Where(x => x.AccountId == account).ToListAsync(ct), await db.MeetingRoomCalendars.Where(x => x.AccountId == account).ToListAsync(ct));
    public async Task<MeetingRoomState> ReadConfigurationAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await Load(db, account, ct);
    }
    public async Task<IMeetingRoomTransaction> BeginConfigurationAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            return new ConfigurationTransaction(db, tx, await Load(db, account, ct));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public Task<IRoomBookingTransaction> BeginRoomAsync(Guid account, Guid room, CancellationToken ct) => BeginBookingTransaction(account, room, null, ct);
    public Task<IRoomBookingTransaction> BeginBookingAsync(Guid account, Guid booking, CancellationToken ct) => BeginBookingTransaction(account, null, booking, ct);
    private async Task<IRoomBookingTransaction> BeginBookingTransaction(Guid account, Guid? roomId, Guid? bookingId, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            if (!roomId.HasValue) roomId = await db.RoomBookings.AsNoTracking().Where(x => x.AccountId == account && x.Id == bookingId).Select(x => (Guid?)x.MeetingRoomId).SingleOrDefaultAsync(ct)
                ?? throw new MeetingRoomValidationException("RoomBookingNotFound");
            var rooms = await db.MeetingRooms.FromSqlInterpolated($"SELECT * FROM meeting_rooms WHERE \"AccountId\" = {account} AND \"Id\" = {roomId.Value} FOR UPDATE").ToListAsync(ct);
            var room = rooms.SingleOrDefault() ?? throw new MeetingRoomValidationException("MeetingRoomNotFound");
            var active = await db.Buildings.AnyAsync(x => x.AccountId == account && x.Id == room.BuildingId && x.IsActive, ct);
            var bookings = await db.RoomBookings.Where(x => x.AccountId == account && x.MeetingRoomId == room.Id).ToListAsync(ct);
            return new BookingTransaction(db, tx, room, active, bookings);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class BookingTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, MeetingRoom room, bool active, List<RoomBooking> bookings) : IRoomBookingTransaction
    {
        private readonly HashSet<Guid> original = bookings.Select(x => x.Id).ToHashSet();
        public MeetingRoom Room => room; public bool BuildingActive => active; public List<RoomBooking> Bookings => bookings;
        public async Task CommitAsync(CancellationToken ct)
        {
            if (bookings.Any(x => x.AccountId != room.AccountId || x.MeetingRoomId != room.Id)) throw new UnauthorizedAccessException();
            db.RoomBookings.AddRange(bookings.Where(x => !original.Contains(x.Id))); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
    private sealed class ConfigurationTransaction : IMeetingRoomTransaction
    {
        private readonly TenantPlatformDbContext db; private readonly IDbContextTransaction tx;
        private readonly List<MeetingRoom> rooms; private readonly List<CalendarIntegration> integrations; private readonly List<MeetingRoomCalendar> mappings;
        public MeetingRoomState State { get; }
        public ConfigurationTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, MeetingRoomState state)
        { this.db = db; this.tx = tx; State = state; rooms = state.Rooms.ToList(); integrations = state.Integrations.ToList(); mappings = state.Mappings.ToList(); }
        public async Task CommitAsync(CancellationToken ct)
        {
            db.MeetingRooms.AddRange(State.Rooms.Except(rooms)); db.CalendarIntegrations.AddRange(State.Integrations.Except(integrations));
            db.MeetingRoomCalendars.AddRange(State.Mappings.Except(mappings)); db.MeetingRoomCalendars.RemoveRange(mappings.Except(State.Mappings));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { throw new MeetingRoomValidationException("MeetingRoomCalendarAlreadyMapped"); }
            await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
