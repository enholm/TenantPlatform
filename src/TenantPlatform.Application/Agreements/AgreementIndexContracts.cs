using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public record AgreementIndexRegister(bool CanWrite, List<AgreementIndex> Indices, List<AgreementIndexValue> Values);
public record AgreementAdjustmentCalculation(AgreementAdjustmentRule? Rule, AgreementPriceVersion BasePrice,
    AgreementPriceVersion CurrentPrice, AgreementLineVersion Line, AgreementIndexValue? BaseIndex,
    AgreementIndexValue? ComparisonIndex, DateOnly ScheduledDate, DateOnly EffectiveDate,
    decimal RawIndexPercent, decimal AppliedPercent, decimal NewPrice, string Fingerprint);
