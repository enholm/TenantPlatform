using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementAdjustmentCalculator
{
    public static (decimal RawIndexPercent, decimal AppliedPercent, decimal Price) Calculate(
        AgreementAdjustmentRule rule, decimal basisPrice, decimal? basisIndex, decimal? comparisonIndex, decimal? chosenPercent)
    {
        if (basisPrice < 0 || basisPrice > 100000000 || !Enum.IsDefined(rule.Kind) ||
            rule.FloorPercent > rule.CeilingPercent || rule.SharePercent is < 0 or > 100 ||
            rule.FloorPercent < -100 || rule.CeilingPercent > 10000 || Math.Abs(rule.AdditionPercent) > 10000 ||
            Math.Abs(rule.FixedPercent) > 10000 || !Enum.IsDefined(rule.Addition)) throw new AgreementValidationException("ProcessingInvalidRule");
        decimal raw = 0, rate = 0;
        if (rule.Kind == AgreementAdjustmentKind.Index)
        {
            if (basisIndex is null or <= 0 || comparisonIndex is null or <= 0) throw new AgreementValidationException("ProcessingMissingIndex");
            raw = (comparisonIndex.Value / basisIndex.Value - 1) * 100;
            rate = raw * rule.SharePercent / 100;
            rate = rule.Addition switch
            {
                AgreementAdjustmentAddition.PercentagePoints => rate + rule.AdditionPercent,
                AgreementAdjustmentAddition.PriceMarkup => ((1 + rate / 100) * (1 + rule.AdditionPercent / 100) - 1) * 100,
                _ => rate
            };
        }
        else if (rule.Kind == AgreementAdjustmentKind.Percentage) rate = rule.FixedPercent;
        else if (rule.Kind == AgreementAdjustmentKind.Limit)
        {
            if (!chosenPercent.HasValue) throw new AgreementValidationException("ProcessingChoiceRequired");
            var lower = rule.FloorPercent ?? (rule.AllowDecrease ? -100 : 0);
            if (!rule.CeilingPercent.HasValue || chosenPercent < lower || chosenPercent > rule.CeilingPercent || (!rule.AllowDecrease && chosenPercent < 0))
                throw new AgreementValidationException("ProcessingOutsideLimit");
            rate = chosenPercent.Value;
        }
        if (rule.Kind == AgreementAdjustmentKind.Fixed) throw new AgreementValidationException("ProcessingFixedPrice");
        if (!rule.AllowDecrease) rate = Math.Max(0, rate);
        if (rule.FloorPercent.HasValue) rate = Math.Max(rule.FloorPercent.Value, rate);
        if (rule.CeilingPercent.HasValue) rate = Math.Min(rule.CeilingPercent.Value, rate);
        var price = decimal.Round(basisPrice * (1 + rate / 100), 2, MidpointRounding.AwayFromZero);
        if (price is < 0 or > 100000000) throw new AgreementValidationException("ProcessingInvalidRule");
        return (raw, rate, price);
    }
    public static DateOnly Period(DateOnly date, AgreementIndexResolution resolution) =>
        new(date.Year, ((date.Month - 1) / (int)resolution) * (int)resolution + 1, 1);
    public static DateOnly Anchor(AgreementAdjustmentRule r, DateOnly agreementStart) => r.Anchor switch
    {
        AgreementAdjustmentAnchor.Anniversary => agreementStart,
        _ => r.AnchorDate
    };
    public static bool IsScheduled(AgreementAdjustmentRule r, DateOnly agreementStart, DateOnly date)
    {
        if (date < r.FirstAllowedDate || date < r.EffectiveFrom || r.IntervalMonths < 1) return false;
        var anchor = Anchor(r, agreementStart);
        var months = (date.Year - anchor.Year) * 12 + date.Month - anchor.Month;
        return months >= 0 && months % r.IntervalMonths == 0 && anchor.AddMonths(months) == date;
    }
}
