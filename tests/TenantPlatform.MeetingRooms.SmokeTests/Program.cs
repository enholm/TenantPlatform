using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.MeetingRooms;

// Run only against a disposable PostgreSQL database with this exact name.
// The web host is never started, so production initialization/email cannot run.
var connection = Environment.GetEnvironmentVariable("MEETING_ROOM_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set MEETING_ROOM_TEST_CONNECTION to a disposable PostgreSQL database.");
var connectionBuilder = new NpgsqlConnectionStringBuilder(connection);
if (connectionBuilder.Database != "tenant_meeting_tests")
    throw new InvalidOperationException("The test database must be named tenant_meeting_tests.");

// Each run owns a new schema; no existing test data is modified or deleted.
var schema = "meeting_test_" + Guid.NewGuid().ToString("N");
await using (var setup = new NpgsqlConnection(connection))
{
    await setup.OpenAsync();
    await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup);
    await command.ExecuteNonQueryAsync();
}
connectionBuilder.SearchPath = schema;
Console.WriteLine($"Test schema: {schema}");
var options = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(connectionBuilder.ConnectionString).Options;
var factory = new TestFactory(options);
await using (var db = factory.CreateDbContext())
{
    Assert(!db.Database.HasPendingModelChanges(), "migration snapshot matches model");
    await db.Database.MigrateAsync();
    Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applied to isolated schema");
}

var accountA = Guid.NewGuid();
var accountB = Guid.NewGuid();
var adminId = Guid.NewGuid();
var memberId = Guid.NewGuid();
var outsiderId = Guid.NewGuid();
var buildingA = Guid.NewGuid();
var buildingB = Guid.NewGuid();
var integrationB = Guid.NewGuid();
var roomB = Guid.NewGuid();
var bookingB = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Accounts.AddRange(new Account { Id = accountA, Name = "Account A" }, new Account { Id = accountB, Name = "Account B" });
    db.Users.AddRange(
        new User { Id = adminId, Email = "admin@example.test", FirstName = "Admin" },
        new User { Id = memberId, Email = "member@example.test", FirstName = "Member" },
        new User { Id = outsiderId, Email = "outsider@example.test", FirstName = "Outsider" });
    var adminMembership = new UserAccount { Id = Guid.NewGuid(), AccountId = accountA, UserId = adminId };
    db.UserAccounts.AddRange(adminMembership,
        new UserAccount { Id = Guid.NewGuid(), AccountId = accountA, UserId = memberId });
    db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = adminMembership.Id, Role = UserRole.AccountAdmin });
    db.Buildings.AddRange(new Building { Id = buildingA, AccountId = accountA, Name = "A" }, new Building { Id = buildingB, AccountId = accountB, Name = "B" });
    db.CalendarIntegrations.Add(new CalendarIntegration { Id = integrationB, AccountId = accountB, Name = "B", Provider = CalendarProvider.Google });
    db.MeetingRooms.Add(new MeetingRoom { Id = roomB, AccountId = accountB, BuildingId = buildingB, Name = "Room B" });
    db.RoomBookings.Add(new RoomBooking
    {
        Id = bookingB, AccountId = accountB, MeetingRoomId = roomB, OrganizerUserId = outsiderId,
        Subject = "Private B", StartUtc = DateTimeOffset.UtcNow, EndUtc = DateTimeOffset.UtcNow.AddHours(1),
        CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
}

var admin = Services(adminId, accountA);
var member = Services(memberId, accountA);
var outsider = Services(outsiderId, accountA);
var roomId = await admin.Rooms.CreateAsync(accountA, Room());
var integrationId = await admin.Integrations.CreateAsync(accountA, new() { Name = "Account calendar", Provider = CalendarProvider.Microsoft365 });
var mappedRoom = Room(); mappedRoom.CalendarIntegrationId = integrationId; mappedRoom.ExternalCalendarId = "room@example.test";
await admin.Rooms.UpdateAsync(accountA, roomId, mappedRoom);
Assert((await admin.Rooms.GetAsync(accountA, roomId))?.CalendarIntegrationId == integrationId, "admin calendar mapping");
Assert((await member.Rooms.GetAsync(accountA, roomId))?.CalendarIntegrationId is null, "member calendar configuration is not exposed");
await Expect<UnauthorizedAccessException>(() => member.Rooms.CreateAsync(accountA, Room()), "member cannot administer rooms");
await Expect<UnauthorizedAccessException>(() => outsider.Rooms.ListAsync(accountA), "non-member denied");
await Expect<UnauthorizedAccessException>(() => admin.Rooms.ListAsync(accountB), "selected-account mismatch denied");
await Expect<UnauthorizedAccessException>(() => member.Integrations.ListAsync(accountA), "member cannot read calendar configuration");
Assert(await member.Rooms.GetAsync(accountA, roomB) is null, "cross-account room is hidden");
Assert(await member.Bookings.GetAsync(accountA, bookingB) is null, "cross-account booking is hidden");
await Expect<MeetingRoomValidationException>(() => member.Bookings.CancelAsync(accountA, bookingB), "cross-account cancellation denied");
var wrongBuilding = Room(); wrongBuilding.BuildingId = buildingB;
await Expect<MeetingRoomValidationException>(() => admin.Rooms.CreateAsync(accountA, wrongBuilding), "cross-account building denied");
mappedRoom.CalendarIntegrationId = integrationB;
await Expect<MeetingRoomValidationException>(() => admin.Rooms.UpdateAsync(accountA, roomId, mappedRoom), "cross-account integration denied");

var start = new DateTimeOffset(2030, 1, 15, 10, 0, 0, TimeSpan.Zero);
SaveRoomBookingRequest Booking(int hourOffset = 0) => new() { Subject = "Planning", StartUtc = start.AddHours(hourOffset), EndUtc = start.AddHours(hourOffset + 1) };
await Expect<MeetingRoomValidationException>(() => member.Bookings.CreateAsync(accountA, roomB, Booking()), "cross-account room cannot be booked");
await Expect<MeetingRoomValidationException>(() => member.Bookings.ListAsync(accountA, roomB, start, start.AddDays(1)), "cross-account room listing denied");
var invalid = Booking(); invalid.EndUtc = invalid.StartUtc;
await Expect<MeetingRoomValidationException>(() => member.Bookings.CreateAsync(accountA, roomId, invalid), "zero-length interval denied");
Assert(await member.Bookings.IsAvailableAsync(accountA, roomId, start, start.AddHours(1)), "empty room available");

// Independent services/DbContexts submit overlapping bookings concurrently.
var outcomes = await Task.WhenAll(Attempt(), Attempt());
Assert(outcomes.Count(x => x.HasValue) == 1, "exactly one concurrent overlapping booking succeeds");
var bookingId = outcomes.Single(x => x.HasValue)!.Value;
Assert(!await member.Bookings.IsAvailableAsync(accountA, roomId, start, start.AddHours(1)), "occupied room unavailable");
var adjacentId = await member.Bookings.CreateAsync(accountA, roomId, Booking(1));
Assert(adjacentId != bookingId, "adjacent half-open intervals allowed");
var boundaryOverlap = Booking(); boundaryOverlap.StartUtc = start.AddMinutes(30); boundaryOverlap.EndUtc = start.AddHours(2);
await Expect<MeetingRoomValidationException>(() => member.Bookings.UpdateAsync(accountA, bookingId, boundaryOverlap), "reschedule overlap denied");
var adminBookingId = await admin.Bookings.CreateAsync(accountA, roomId, Booking(3));
await Expect<UnauthorizedAccessException>(() => member.Bookings.CancelAsync(accountA, adminBookingId), "cannot cancel another organizer's booking");
await Expect<UnauthorizedAccessException>(() => member.Bookings.UpdateAsync(accountA, adminBookingId, Booking(4)), "cannot edit another organizer's booking");
await member.Bookings.UpdateAsync(accountA, bookingId, Booking(5));
Assert((await member.Bookings.GetAsync(accountA, bookingId))?.StartUtc == start.AddHours(5), "organizer can reschedule");
await member.Bookings.CancelAsync(accountA, bookingId);
await member.Bookings.CancelAsync(accountA, bookingId);
Assert((await member.Bookings.GetAsync(accountA, bookingId))?.Status == RoomBookingStatus.Cancelled, "cancel is idempotent and keeps history");
await Expect<MeetingRoomValidationException>(() => member.Bookings.UpdateAsync(accountA, bookingId, Booking(6)), "cancelled bookings cannot be edited");
await admin.Bookings.CancelAsync(accountA, adjacentId);
Assert(await member.Bookings.IsAvailableAsync(accountA, roomId, start, start.AddHours(2)), "cancellation releases availability");
Assert((await member.Bookings.ListAsync(accountA, roomId, start.Date, start.Date.AddDays(1))).Count == 3, "day list includes booking history");
await admin.Rooms.DeactivateAsync(accountA, roomId);
await Expect<MeetingRoomValidationException>(() => member.Bookings.CreateAsync(accountA, roomId, Booking(8)), "inactive room cannot be booked");
Assert((await member.Rooms.ListAsync(accountA)).Count == 0, "active-room list excludes deactivated room");
Assert((await member.Rooms.ListAsync(accountA, true)).Count == 1, "inactive room remains accessible for cancellation/history");

await using (var db = factory.CreateDbContext())
{
    db.MeetingRoomCalendars.Add(new MeetingRoomCalendar
    {
        Id = Guid.NewGuid(), AccountId = accountB, MeetingRoomId = roomId,
        CalendarIntegrationId = integrationB, ExternalCalendarId = "cross-account"
    });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "database rejects cross-account mapping");
}
await using (var db = factory.CreateDbContext())
{
    db.RoomBookings.Add(new RoomBooking
    {
        Id = Guid.NewGuid(), AccountId = accountA, MeetingRoomId = roomId, OrganizerUserId = memberId,
        Subject = "Invalid", StartUtc = start, EndUtc = start, CreatedUtc = start, UpdatedUtc = start
    });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "database rejects invalid interval");
}
Console.WriteLine("All meeting-room smoke tests passed.");

SaveMeetingRoomRequest Room() => new() { Name = "Meeting room", BuildingId = buildingA, Capacity = 8 };
(IMeetingRoomService Rooms, IRoomBookingService Bookings, ICalendarIntegrationService Integrations) Services(Guid userId, Guid accountId)
{
    var current = new TestUserContext(userId, accountId);
    var authorization = new TenantAuthorizationService(factory, current);
    return (new MeetingRoomService(factory, current, authorization),
        new RoomBookingService(factory, current, authorization),
        new CalendarIntegrationService(factory, current, authorization));
}
async Task<Guid?> Attempt()
{
    try { return await Services(memberId, accountA).Bookings.CreateAsync(accountA, roomId, Booking()); }
    catch (MeetingRoomValidationException ex) when (ex.Message == "RoomBookingOverlap") { return null; }
}
static void Assert(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
}
static async Task Expect<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine($"PASS: {name}"); return; }
    throw new Exception($"FAIL: {name}: expected {typeof(T).Name}");
}
sealed class TestFactory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{
    public TenantPlatformDbContext CreateDbContext() => new(options);
}
sealed class TestUserContext(Guid userId, Guid accountId) : ICurrentUserContextService
{
    public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = userId, CurrentAccountId = accountId };
}
