using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Web.Services.MeetingRooms;

// Future adapters publish local bookings. No adapter is called or registered until
// authenticated integration and reliable delivery/retry support are implemented.
public interface ICalendarProvider
{
    CalendarProvider Provider { get; }
    Task<CalendarAvailabilityResult> GetAvailabilityAsync(
        CalendarIntegration integration, MeetingRoomCalendar calendar,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<ExternalCalendarEventResult> CreateBookingAsync(
        CalendarIntegration integration, MeetingRoomCalendar calendar,
        RoomBooking booking, CancellationToken cancellationToken = default);
    Task UpdateBookingAsync(
        CalendarIntegration integration, MeetingRoomCalendar calendar,
        string externalEventId, RoomBooking booking, CancellationToken cancellationToken = default);
    Task DeleteBookingAsync(
        CalendarIntegration integration, MeetingRoomCalendar calendar,
        string externalEventId, CancellationToken cancellationToken = default);
}

public record CalendarBusyPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public record CalendarAvailabilityResult(IReadOnlyList<CalendarBusyPeriod> BusyPeriods);
public record ExternalCalendarEventResult(string ExternalEventId);
