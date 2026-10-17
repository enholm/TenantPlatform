using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agreements;

public interface IAgreementListService
{
    Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken ct = default);
}

public sealed class AgreementListService(IAuthorizationService authorization, IAgreementListData data, TimeProvider clock) : IAgreementListService
{
    public async Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken ct = default)
    {
        // Blazor callers may keep editing a filter while the operation awaits I/O.
        var query = new AgreementListQuery(filter.Search, filter.Status, filter.Type, filter.OwnerUserId, filter.Page);
        var attentionKind = filter.Attention;
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        AgreementAttentionCriteria? attention = null;
        if (attentionKind.HasValue)
        {
            var zone = await data.GetReminderTimeZoneAsync(accountId, ct);
            attention = AgreementAttentionRules.Create(attentionKind.Value, AgreementReminderSchedule.Today(clock.GetUtcNow(), zone));
        }
        return await data.QueryAsync(scope, query, attention, ct);
    }
}
