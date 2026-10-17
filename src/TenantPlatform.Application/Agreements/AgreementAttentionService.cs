using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agreements;

public sealed record AgreementAttentionDto(DateOnly Today, int Notice, int Renewal, int Expired);
public sealed record AgreementAttentionQuery(DateOnly Today, AgreementAttentionCriteria Notice,
    AgreementAttentionCriteria Renewal, AgreementAttentionCriteria Expired);

public interface IAgreementAttentionData
{
    Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct);
    Task<AgreementAttentionDto> CountAsync(AgreementListScope scope, AgreementAttentionQuery query, CancellationToken ct);
}

public interface IAgreementAttentionService
{
    Task<AgreementAttentionDto> GetAttentionAsync(Guid accountId, CancellationToken ct = default);
}

public sealed class AgreementAttentionService(IAuthorizationService authorization, IAgreementAttentionData data,
    TimeProvider clock) : IAgreementAttentionService
{
    public async Task<AgreementAttentionDto> GetAttentionAsync(Guid accountId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        // The shared evaluator captures the trusted actor synchronously before its first await.
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        var zone = await data.GetReminderTimeZoneAsync(scope.AccountId, ct);
        var today = AgreementReminderSchedule.Today(now, zone);
        return await data.CountAsync(scope, new(today,
            AgreementAttentionRules.Create(AgreementAttention.Notice, today),
            AgreementAttentionRules.Create(AgreementAttention.Renewal, today),
            AgreementAttentionRules.Create(AgreementAttention.Expired, today)), ct);
    }
}
