using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;

public record AgreementIndexRegister(bool CanWrite, List<AgreementIndex> Indices, List<AgreementIndexValue> Values);
public record AgreementAdjustmentCalculation(AgreementAdjustmentRule Rule, AgreementPriceVersion BasePrice,
    AgreementPriceVersion CurrentPrice, AgreementLineVersion Line, AgreementIndexValue? BaseIndex,
    AgreementIndexValue? ComparisonIndex, DateOnly ScheduledDate, DateOnly EffectiveDate,
    decimal RawIndexPercent, decimal AppliedPercent, decimal NewPrice, string Fingerprint);
public record AgreementAdjustmentCandidate(Guid LineId, string Name, DateOnly Date, Guid RuleId, string? BlockedKey);
public record AgreementProcessingView(bool CanEdit, bool CanApprove, List<AgreementAdjustmentRule> Rules,
    List<AgreementAdjustmentProposal> Proposals);
public record AgreementBasisEventData(string EventKey, Guid LineId, List<AgreementCalculatedPeriod> Segments,
    decimal Amount, decimal PreviousAmount, decimal TargetAmount, List<AgreementAdjustmentProposal> Adjustments);
public record AgreementBasisData(Guid AccountId, string AccountName, Guid AgreementId, string AgreementTitle,
    Guid CounterpartyId, string CounterpartyName, AgreementDirection Direction, string Currency,
    string TaxTreatment, List<AgreementBasisEventData> Events, List<Guid> PreviousSnapshotIds);
public record AgreementBasisDetails(AgreementBasis Basis, List<AgreementBasisSnapshot> Snapshots, bool Stale,
    bool NeedsCorrection, bool CanEdit, bool CanApprove);
public record AgreementBasisRun(List<Guid> Created, List<Guid> Existing);
public record AgreementBasisSummary(AgreementBasis Basis, string Title, string Counterparty, string Currency, decimal Amount);
