using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public static class LeasingRules
{
    private static bool Precision(decimal value, int places) => decimal.Round(value, places) == value;
    public static void ValidateCommon(string name, string number, string currency, string? notes, LeasingTerms terms)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(number) || number.Length > 100 || notes?.Length > 10000 ||
            !AgreementPeriodCalculator.Currencies.Contains(currency)) throw new LeasingValidationException("LeasingInvalidDetails");
        if (terms.Months is < 1 or > 600 || !Enum.IsDefined(terms.InterestKind) || !Enum.IsDefined(terms.PaymentFrequency)) throw new LeasingValidationException("LeasingInvalidTerms");
        if (terms.InterestKind == LeasingInterestKind.Reference)
        {
            if (string.IsNullOrWhiteSpace(terms.ReferenceRateName) || terms.ReferenceRateName.Length > 100 || terms.MarginPercentagePoints is null or < -100 or > 100 ||
                !Precision(terms.MarginPercentagePoints.Value, 4)) throw new LeasingValidationException("LeasingInvalidTerms");
            terms.AnnualRatePercent = null;
        }
        else
        {
            if (terms.AnnualRatePercent is null or < 0 or > 100 || !Precision(terms.AnnualRatePercent.Value, 4)) throw new LeasingValidationException("LeasingInvalidTerms");
            terms.ReferenceRateName = null; terms.MarginPercentagePoints = null;
        }
    }
}
