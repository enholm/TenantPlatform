namespace TenantPlatform.Application.Agreements;


// Application decides this scope from verified facts. It is not a transport input contract.
public sealed record AgreementListScope(Guid AccountId, Guid UserId, bool CanReadAll);
public sealed record AgreementListQuery(string? Search, TenantPlatform.Core.Agreements.AgreementStatus? Status,
    TenantPlatform.Core.Agreements.AgreementType? Type, Guid? OwnerUserId, int Page);

public interface IAgreementListData
{
    Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct);
    Task<AgreementPageDto> QueryAsync(AgreementListScope scope, AgreementListQuery query,
        AgreementAttentionCriteria? attention, CancellationToken ct);
}
