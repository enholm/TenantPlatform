namespace TenantPlatform.Web.Services.MeetingRooms;

public interface ICalendarIntegrationService
{
    Task<List<CalendarIntegrationDto>> ListAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid accountId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid accountId, Guid integrationId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default);
}
