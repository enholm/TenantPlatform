using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agreements;

public interface IAgreementInitializationData
{
    Task EnsureVisibleAsync(AgreementListScope scope, Guid? agreementId, CancellationToken ct);
}

// Explicit compatibility writes, callable independently of any GUI authorization.
// Read recipients may request the existing automatic system backfill for visible agreements.
public sealed class AgreementInitializationService(IAuthorizationService authorization, IAgreementInitializationData data)
{
    public async Task EnsureAgreementAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(accountId, agreementId, ct);
        await data.EnsureVisibleAsync(scope.ListScope, agreementId, ct);
    }

    public async Task EnsureVisibleAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        await data.EnsureVisibleAsync(scope, null, ct);
    }
}
