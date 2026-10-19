using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agreements;

public interface IAgreementDetailsData
{
    Task<AgreementDetailsDto> ReadAsync(AgreementReadScope scope, CancellationToken ct);
    Task<AgreementReportRow> ReadSummaryAsync(AgreementReadScope scope, CancellationToken ct);
}

// This use case never initializes deadlines or invokes a writer.
public interface IAgreementDetailsReader
{
    Task<AgreementDetailsDto> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task<AgreementReportRow> ReadSummaryAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
}

public sealed class AgreementDetailsReader(IAuthorizationService authorization, IAgreementDetailsData data) : IAgreementDetailsReader
{
    public async Task<AgreementReportRow> ReadSummaryAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(accountId, agreementId, ct);
        return await data.ReadSummaryAsync(scope, ct);
    }
    public async Task<AgreementDetailsDto> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(accountId, agreementId, ct);
        return await data.ReadAsync(scope, ct);
    }
}
