using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.MeetingRooms;

public class CalendarIntegrationService(
    IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization) : ICalendarIntegrationService
{
    public async Task<List<CalendarIntegrationDto>> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, true, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CalendarIntegrations.AsNoTracking().Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Name).Select(x => new CalendarIntegrationDto
            { Id = x.Id, Name = x.Name, Provider = x.Provider, IsActive = x.IsActive }).ToListAsync(cancellationToken);
    }

    public Task<Guid> CreateAsync(Guid accountId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync(accountId, null, request, cancellationToken);

    public async Task UpdateAsync(Guid accountId, Guid integrationId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) =>
        await SaveAsync(accountId, integrationId, request, cancellationToken);

    private async Task<Guid> SaveAsync(Guid accountId, Guid? id, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 || !Enum.IsDefined(request.Provider))
            throw new MeetingRoomValidationException("CalendarIntegrationInvalidDetails");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var integration = id.HasValue
            ? await db.CalendarIntegrations.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == id.Value, cancellationToken)
                ?? throw new MeetingRoomValidationException("CalendarIntegrationNotFound")
            : new CalendarIntegration { Id = Guid.NewGuid(), AccountId = accountId };
        if (!id.HasValue) db.CalendarIntegrations.Add(integration);
        integration.Name = request.Name.Trim();
        integration.Provider = request.Provider;
        integration.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return integration.Id;
    }
}
