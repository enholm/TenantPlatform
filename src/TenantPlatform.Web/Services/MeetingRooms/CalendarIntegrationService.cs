using TenantPlatform.Application.MeetingRooms;
using TenantPlatform.Application.Buildings;
namespace TenantPlatform.Web.Services.MeetingRooms;
public sealed class CalendarIntegrationService(TenantPlatform.Application.MeetingRooms.CalendarIntegrationService service) : ICalendarIntegrationService
{
    public Task<List<CalendarIntegrationDto>> ListAsync(Guid accountId, CancellationToken cancellationToken = default) => service.ListAsync(accountId, cancellationToken);
    public Task<Guid> CreateAsync(Guid accountId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) => service.CreateAsync(accountId, request, cancellationToken);
    public Task UpdateAsync(Guid accountId, Guid integrationId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) => service.UpdateAsync(accountId, integrationId, request, cancellationToken);
}
