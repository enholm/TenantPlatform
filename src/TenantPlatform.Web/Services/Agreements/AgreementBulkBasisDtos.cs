namespace TenantPlatform.Web.Services.Agreements;

public enum AgreementBulkBasisStatus { Ready, Existing, Followup, Empty, Created }
public record AgreementBulkBasisOption(Guid Id, string Title, Guid CounterpartyId, string Counterparty);
public record AgreementBulkBasisFilter(DateOnly From, DateOnly To, Guid? CounterpartyId = null, Guid? AgreementId = null);
public record AgreementBulkBasisSelection(Guid AgreementId, string Fingerprint);
public record AgreementBulkBasisLink(Guid Id, DateOnly InvoiceDate, decimal Amount);
public record AgreementBulkBasisPreview(Guid AgreementId, string Title, string Counterparty, string Currency,
    AgreementBulkBasisStatus Status, string? Reason, bool CanGenerate, string Fingerprint,
    List<AgreementCalculatedPeriod> Periods, List<AgreementCalculatedPeriod> NewPeriods,
    List<AgreementBulkBasisLink> Existing)
{
    public int BasisCount => NewPeriods.Select(x => x.InvoiceDate).Distinct().Count() + Existing.Count;
    public int LineCount => Periods.Select(x => x.EventKey).Distinct().Count();
    public decimal Amount => Periods.Sum(x => x.Amount);
    public decimal NewAmount => NewPeriods.Sum(x => x.Amount);
}
public record AgreementBulkBasisResult(Guid AgreementId, AgreementBulkBasisStatus Status, string? Reason,
    string Currency, List<AgreementBulkBasisLink> Created, List<AgreementBulkBasisLink> Existing);
