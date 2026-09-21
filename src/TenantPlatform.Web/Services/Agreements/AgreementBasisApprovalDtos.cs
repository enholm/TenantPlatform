using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public record AgreementBasisApprovalFilter(Guid? CounterpartyId = null, Guid? AgreementId = null,
    DateOnly? From = null, DateOnly? To = null, AgreementDirection? Direction = null);
public record AgreementBasisApprovalSelection(Guid BasisId, int Revision);
public record AgreementBasisApprovalItem(Guid BasisId, int Revision, Guid AgreementId, string Title,
    Guid CounterpartyId, string Counterparty, AgreementDirection Direction, DateOnly InvoiceDate,
    DateOnly? PeriodFrom, DateOnly? PeriodTo, decimal Amount, string Currency, bool IsCorrection,
    bool CanApprove, string? Reason);
public record AgreementBasisApprovalList(List<AgreementBasisApprovalItem> Items, List<AgreementBulkBasisOption> Agreements);
public enum AgreementBasisApprovalOutcome { Approved, AlreadyProcessed, Failed }
public record AgreementBasisApprovalResult(Guid BasisId, AgreementBasisApprovalOutcome Outcome, string? Reason);
