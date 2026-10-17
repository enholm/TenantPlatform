using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public sealed record AgreementDashboardItem(Guid Id, string Title, AgreementType Type, DateOnly? EndDate);
public sealed record AgreementDashboardList(DateOnly Today, IReadOnlyList<AgreementDashboardItem> Items);
public sealed record AgreementPriorityQuery(DateOnly Today, AgreementAttentionCriteria Expired, AgreementAttentionCriteria Notice);

public static class AgreementPriorityRules
{
    public const int Limit = 5;
    public static AgreementPriorityQuery Create(DateOnly today) => new(today,
        AgreementAttentionRules.Create(AgreementAttention.Expired, today),
        AgreementAttentionRules.Create(AgreementAttention.Notice, today));
}

public interface IAgreementDashboardData
{
    Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct);
    Task<AgreementDashboardList> QueryAsync(AgreementListScope scope, AgreementPriorityQuery query, CancellationToken ct);
}

public interface IAgreementDashboardService
{
    Task<AgreementDashboardList> GetDashboardContractsAsync(Guid accountId, CancellationToken ct = default);
}

public sealed class AgreementDashboardService(IAuthorizationService authorization, IAgreementDashboardData data,
    TimeProvider clock) : IAgreementDashboardService
{
    public async Task<AgreementDashboardList> GetDashboardContractsAsync(Guid accountId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        // Shared authorization captures the actor synchronously before its first await.
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        var zone = await data.GetReminderTimeZoneAsync(scope.AccountId, ct);
        var today = AgreementReminderSchedule.Today(now, zone);
        return await data.QueryAsync(scope, AgreementPriorityRules.Create(today), ct);
    }
}
