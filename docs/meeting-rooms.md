# Meeting rooms and local booking

TenantPlatform is the booking source of truth. The first version stores rooms,
confirmed/cancelled bookings, account calendar configurations, and optional room
calendar mappings. It does not authenticate to, query, or publish to external calendars.

## UI and permissions

| Route | Access | Operations |
| --- | --- | --- |
| `/admin/meeting-rooms` | AccountAdmin in selected account | List/create/edit rooms, capacity, building, activation, optional calendar mapping |
| `/admin/calendar-integrations` | AccountAdmin in selected account | Create/edit/activate/deactivate named Microsoft 365 or Google configurations |
| `/meeting-rooms` | Membership in selected account | List rooms/day bookings, availability, create, edit/cancel own booking |

AccountAdmin can also edit/cancel other organizers' bookings in the selected account.
PlatformAdmin alone does not grant account membership or room administration.
PropertyAdmin has the same booking access as other account members, but no room
configuration privileges, matching account-level service-definition administration.
Permissions use the existing `ITenantAuthorizationService`. Services check identity,
selected account, and permission before database access. Client-provided room,
building, calendar, and booking IDs are constrained to that account.

Dates are explicitly entered/displayed in UTC in this first UI. No server-local or
browser-local time zone is silently assumed. Bookings can span days; the day view
includes every booking intersecting the selected UTC day. Existing/inactive rooms
remain visible for history and cancellation; inactive rooms/buildings reject new or
rescheduled bookings. Subject/description can be edited, but a booking cannot be
moved to another room or restored after cancellation. Rooms and integrations are
deactivated, and bookings are cancelled; historical bookings are not physically deleted.

## Persistence and concurrency

Each operation creates/disposes its own context through `IDbContextFactory`.
Booking create/update/cancel use an explicit READ COMMITTED transaction and lock
the target meeting-room row with `SELECT ... FOR UPDATE`. The overlap check runs
after obtaining that lock and before saving. This serializes writers for the same
room across circuits and app instances. Intervals are half-open: `existing.Start <
new.End && existing.End > new.Start`, and cancelled bookings do not block time.
Availability checks outside a write transaction are advisory.

The lock is held until transaction end, following
[PostgreSQL row-lock semantics](https://www.postgresql.org/docs/current/explicit-locking.html#LOCKING-ROWS).
All future booking write paths must preserve this lock protocol. Direct SQL/imports
that bypass the service can still insert overlaps; no exclusion constraint or
additional PostgreSQL extension is introduced. Database check constraints enforce
positive capacity and `EndUtc > StartUtc`; composite foreign keys protect the account
boundary for room bookings and room/calendar mappings. Building ownership is checked
by the service, consistent with existing building relationships.

One external mapping per room is supported initially. A calendar within an account
integration can map to only one room. No token/secret fields exist. `ICalendarProvider`
is a future adapter contract with explicit integration/mapping context and external
event IDs. No provider is registered or called, avoiding any suggestion that local
bookings have already synchronized. OAuth, secure credentials, delivery/retries, and
persisted event mappings remain work for the real integrations.

## Migration and validation

Generated using `bash scripts/add-migration.sh AddMeetingRoomsAndBookings`:
`20260910185655_AddMeetingRoomsAndBookings`. Existing migrations are untouched.
The existing DatabaseInitializer will apply it at the next authorized application
startup. This implementation was validated in an isolated test schema; the configured
application database was not migrated or started.

Build: `dotnet build TenantPlatform.sln`.

The standalone smoke-test executable needs no additional test framework packages.
Set `MEETING_ROOM_TEST_CONNECTION` to a disposable PostgreSQL database named exactly
`tenant_meeting_tests`, then run:

```sh
dotnet run --project tests/TenantPlatform.MeetingRooms.SmokeTests
```

The database user needs permission to create a schema and tables. Each run creates a
new `meeting_test_<guid>` schema and applies the complete migration chain there;
existing schemas/data are not removed. The test does not start the Web host or email
workers. Tests cover mapping/room/account access, organizer privileges, invalid
intervals, overlapping concurrent creation, adjacent intervals, rescheduling,
cancellation, inactive rooms, database constraints, and snapshot/migration consistency.

## File inventory

New files, relative to the repository root:

- `src/TenantPlatform.Core/MeetingRooms/`: `MeetingRoom.cs`, `RoomBooking.cs`,
  `RoomBookingStatus.cs`, `CalendarIntegration.cs`, `CalendarProvider.cs`, `MeetingRoomCalendar.cs`.
- `src/TenantPlatform.Infrastructure/Persistence/Configurations/`:
  `MeetingRoomConfiguration.cs`, `RoomBookingConfiguration.cs`,
  `CalendarIntegrationConfiguration.cs`, `MeetingRoomCalendarConfiguration.cs`.
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/`:
  `20260910185655_AddMeetingRoomsAndBookings.cs` and its `.Designer.cs` file.
- `src/TenantPlatform.Web/Services/MeetingRooms/`: `IMeetingRoomService.cs`,
  `MeetingRoomService.cs`, `MeetingRoomDtos.cs`, `IRoomBookingService.cs`,
  `RoomBookingService.cs`, `RoomBookingDtos.cs`, `ICalendarIntegrationService.cs`,
  `CalendarIntegrationService.cs`, `CalendarIntegrationDtos.cs`, `ICalendarProvider.cs`,
  `MeetingRoomAccess.cs`, `MeetingRoomValidationException.cs`.
- `src/TenantPlatform.Web/Components/Pages/MeetingRooms/`:
  `ManageMeetingRooms.razor`, `CalendarIntegrations.razor`, `RoomBookings.razor`.
- `tests/TenantPlatform.MeetingRooms.SmokeTests/`: `Program.cs`,
  `TenantPlatform.MeetingRooms.SmokeTests.csproj`.
- `docs/meeting-rooms.md` (this file).

Updated files:

- Infrastructure `Persistence/TenantPlatformDbContext.cs` and
  `Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs`: sets and generated model.
- Web `Program.cs`: service registration.
- Web `Security/Authorization/ITenantAuthorizationService.cs`,
  `TenantAuthorizationService.cs`, `NavigationPermissions.cs`: room permissions/navigation.
- Web `Components/Layout/NavMenu.razor`: booking and administration links.
- Web `Services/Buildings/BuildingService.cs` and `Services/Accounts/AccountService.cs`:
  deletion guards for new references.
- Web `Resources/TenantPlatformResources.nb-NO.resx`, `.en-GB.resx`, `.sv-SE.resx`:
  localized pages, validation messages, status names, and provider labels.
