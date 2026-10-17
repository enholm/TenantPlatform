using TenantPlatform.Core.MeetingRooms;
namespace TenantPlatform.Application.MeetingRooms;

public sealed class CalendarIntegrationService(MeetingRoomAccess access, IMeetingRoomData data) : ICalendarIntegrationService
{
    public async Task<List<CalendarIntegrationDto>> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(accountId, true, cancellationToken);
        return (await data.ReadConfigurationAsync(accountId, cancellationToken)).Integrations.OrderBy(x => x.Name)
            .Select(x => new CalendarIntegrationDto { Id = x.Id, Name = x.Name, Provider = x.Provider, IsActive = x.IsActive }).ToList();
    }
    public Task<Guid> CreateAsync(Guid accountId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) => Save(accountId, null, request, cancellationToken);
    public async Task UpdateAsync(Guid accountId, Guid integrationId, SaveCalendarIntegrationRequest request, CancellationToken cancellationToken = default) => _ = await Save(accountId, integrationId, request, cancellationToken);
    private async Task<Guid> Save(Guid account, Guid? id, SaveCalendarIntegrationRequest input, CancellationToken ct)
    {
        var name = input.Name; var provider = input.Provider; var active = input.IsActive;
        var actor = await access.RequireAsync(account, true, ct);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || !Enum.IsDefined(provider)) throw new MeetingRoomValidationException("CalendarIntegrationInvalidDetails");
        await using var tx = await data.BeginConfigurationAsync(account, ct);
        await access.RevalidateAsync(actor, true, ct);
        var entity = id.HasValue ? tx.State.Integrations.SingleOrDefault(x => x.Id == id) ?? throw new MeetingRoomValidationException("CalendarIntegrationNotFound")
            : new CalendarIntegration { Id = Guid.NewGuid(), AccountId = account };
        entity.Name = name.Trim(); entity.Provider = provider; entity.IsActive = active;
        if (!id.HasValue) tx.State.Integrations.Add(entity);
        await tx.CommitAsync(ct); return entity.Id;
    }
}
