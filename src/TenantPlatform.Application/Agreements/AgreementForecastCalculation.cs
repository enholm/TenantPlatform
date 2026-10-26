using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

// Shared pure calculation for GUI, bases and bounded read transports. No persistence.
public static class AgreementForecastCalculation
{
    public static List<AgreementCalculatedPeriod> Calculate(AgreementFinancialState state, DateOnly from, DateOnly to, CancellationToken ct = default, int maxPeriods = 20000)
    {
        var a = state.Agreement;
        var versions = state.Versions.Where(x => x.Status != AgreementLineStatus.Draft).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        foreach (var v in versions) v.Documents = v.Documents.OrderBy(x => x.DocumentId).ToList();
        var sequences = versions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        var prices = state.Prices.Where(x => x.Independent || sequences.Contains((x.LineId, x.Sequence))).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        var automatic = AgreementAutomaticIndexCalculator.Project(a.IndexSetupNeedsReview, versions, prices, state.Selections.OrderBy(x => x.Sequence).ToList(), state.Indices, state.IndexValues.Where(x => !x.Superseded).ToList(), ct);
        return AgreementPeriodCalculator.Calculate(a.Id, a.Direction ?? throw new AgreementValidationException("FinanceSetupRequired"), a.Currency ?? throw new AgreementValidationException("FinanceSetupRequired"), versions, prices, from, to, automatic, ct, maxPeriods);
    }
}
