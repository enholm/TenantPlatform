using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public sealed record FinancePage<T>(int RequestedPage, int Page, int TotalCount, T[] Items)
{
    public int PageSize => 25;
    public int ReturnedCount => Items.Length;
    public bool HasMore => (long)Page * PageSize < TotalCount;
    public int? NextPage => HasMore && Page < 10000 ? Page + 1 : null;
    public bool Complete => TotalCount == Items.Length;
    public bool LimitReached => HasMore && Page >= 10000;
}
public sealed record FinanceLineVersion(Guid Id, Guid LineId, int Sequence, DateOnly EffectiveFrom, DateTimeOffset RecordedUtc,
    string Name, string? Description, DateOnly StartDate, DateOnly? EndDate, DateOnly FirstPayableDate,
    Guid? PayableSourceLineId, DateOnly? PayableSourceStartDate, int? PayableOffsetMonths, AgreementFrequency Frequency,
    AgreementAnchor Anchor, DateOnly AnchorDate, AgreementBillingTiming BillingTiming, AgreementLineStatus Status, bool IndexRegulated);
public sealed record FinancePrice(Guid Id, Guid LineId, int Sequence, bool Independent, DateOnly EffectiveFrom,
    DateOnly? IndexBaseDate, decimal Quantity, decimal UnitPrice, DateTimeOffset RecordedUtc);
public sealed record FinanceLines(AgreementDirection? Direction, string? Currency, int LineCount,
    FinancePage<FinanceLineVersion> Versions, FinancePage<FinancePrice> Prices);
public sealed record FinancePeriod(Guid LineId, string LineName, Guid LineVersionId, Guid PriceVersionId,
    AgreementDirection Direction, string Currency, DateOnly ReferenceFrom, DateOnly ReferenceTo, DateOnly From, DateOnly To,
    DateOnly InvoiceDate, decimal Quantity, decimal UnitPrice, int ActiveDays, int ReferenceDays, decimal Amount, bool Included);
public sealed record FinanceForecast(DateOnly From, DateOnly To, AgreementDirection Direction, string Currency,
    bool IndexSetupNeedsReview, decimal Income, decimal Cost, FinancePeriod[] Periods);
public sealed class AgreementFinanceLimitException : Exception;
public interface IAgreementFinanceReadData
{
    Task<FinanceLines> LinesAsync(AgreementReadScope scope, Guid? line, int versionsPage, int pricesPage, CancellationToken ct);
    Task<AgreementFinancialState> ForecastStateAsync(AgreementReadScope scope, CancellationToken ct);
}
public sealed class AgreementFinanceReader(IAuthorizationService authorization, IAgreementFinanceReadData data)
{
    // Public limits are transport-neutral. Overflow is a whole-result error, never partial facts.
    public const int MaxVersions = 250, MaxPrices = 500, MaxSelections = 100, MaxIndices = 100, MaxValues = 1000, MaxPeriods = 500;
    public async Task<FinanceLines> LinesAsync(Guid account, Guid agreement, Guid? line = null, int versionsPage = 1, int pricesPage = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (line == Guid.Empty || versionsPage is < 1 or > 10000 || pricesPage is < 1 or > 10000) throw new ArgumentException();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        var result = await data.LinesAsync(scope, line, versionsPage, pricesPage, ct);
        if ((await authorization.GetAgreementReadScopeAsync(account, agreement, ct)).ListScope.UserId != scope.ListScope.UserId) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        return result;
    }
    public async Task<FinanceForecast> ForecastAsync(Guid account, Guid agreement, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == default || from > to || to.Year > 9998 || to.DayNumber - from.DayNumber > 366) throw new ArgumentException();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        var state = await data.ForecastStateAsync(scope, ct);
        if ((await authorization.GetAgreementReadScopeAsync(account, agreement, ct)).ListScope.UserId != scope.ListScope.UserId) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        var rows = AgreementForecastCalculation.Calculate(state, from, to, ct, MaxPeriods);
        ct.ThrowIfCancellationRequested();
        if (rows.Count > MaxPeriods) throw new AgreementFinanceLimitException();
        return new(from, to, state.Agreement.Direction!.Value, state.Agreement.Currency!, state.Agreement.IndexSetupNeedsReview,
            rows.Where(x => x.Direction == AgreementDirection.Income).Sum(x => x.Amount), rows.Where(x => x.Direction == AgreementDirection.Cost).Sum(x => x.Amount),
            rows.Select(x => new FinancePeriod(x.LineId, x.LineName, x.LineVersionId, x.PriceVersionId, x.Direction, x.Currency,
                x.ReferenceFrom, x.ReferenceTo, x.From, x.To, x.InvoiceDate, x.Quantity, x.UnitPrice, x.ActiveDays, x.ReferenceDays, x.Amount, x.Included)).ToArray());
    }
}
