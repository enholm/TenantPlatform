using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementAdjustmentCalculator
{
    public static (decimal RawIndexPercent, decimal AppliedPercent, decimal Price) Calculate(decimal price, decimal basis, decimal comparison)
    {
        if (price is < 0 or > 100000000 || basis <= 0 || comparison <= 0) throw new AgreementValidationException("ProcessingInvalidIndex");
        var rate = (comparison / basis - 1) * 100;
        var adjusted = decimal.Round(price * comparison / basis, 2, MidpointRounding.AwayFromZero);
        if (adjusted is < 0 or > 100000000) throw new AgreementValidationException("FinanceInvalidLine");
        return (rate, rate, adjusted);
    }
    public static DateOnly Period(DateOnly date, AgreementIndexResolution resolution) =>
        new(date.Year, ((date.Month - 1) / (int)resolution) * (int)resolution + 1, 1);
}
