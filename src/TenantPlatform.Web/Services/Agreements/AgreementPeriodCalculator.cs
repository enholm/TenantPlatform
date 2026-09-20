using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public record AgreementCalculatedPeriod(Guid AgreementId, Guid LineId, string LineName,
    AgreementDirection Direction, string Currency, DateOnly ReferenceFrom, DateOnly ReferenceTo,
    DateOnly From, DateOnly To, DateOnly InvoiceDate, decimal Quantity, decimal UnitPrice,
    int ActiveDays, int ReferenceDays, decimal Amount, bool Included, Guid LineVersionId,
    Guid PriceVersionId, string EventKey, IReadOnlyList<Guid> DocumentIds)
{
    public IReadOnlyList<AgreementAutomaticAdjustment> IndexAdjustments { get; init; } = [];
    public decimal Fraction => (decimal)ActiveDays / ReferenceDays;
    public string ExplanationKey => Included ? "FinanceIncluded" : "FinanceCalculationText";
}

/// <summary>Pure preview; input/output dates are inclusive. Calculation uses exclusive upper bounds.</summary>
public static class AgreementPeriodCalculator
{
    public static IReadOnlyList<string> Currencies { get; } = Array.AsReadOnly(new[] { "NOK", "SEK", "DKK", "EUR", "GBP", "USD" });

    public static (DateOnly From, DateOnly Until) Reference(AgreementLineVersion v, DateOnly date)
    {
        if (v.Frequency == AgreementFrequency.Once) return (date, date.AddDays(1));
        var months = (int)v.Frequency;
        var anchor = v.Anchor == AgreementAnchor.Calendar ? new DateOnly(1, 1, 1) : v.AnchorDate;
        var difference = (date.Year - anchor.Year) * 12 + date.Month - anchor.Month;
        var n = (int)Math.Floor((decimal)difference / months);
        var from = anchor.AddMonths(n * months);
        if (from > date) from = anchor.AddMonths(--n * months);
        var until = anchor.AddMonths((n + 1) * months);
        if (date >= until) { from = until; until = anchor.AddMonths((n + 2) * months); }
        return (from, until);
    }

    public static bool IsBoundary(AgreementLineVersion v, DateOnly date) =>
        v.Frequency != AgreementFrequency.Once && Reference(v, date).From == date;

    public static List<AgreementCalculatedPeriod> Calculate(Guid agreementId, AgreementDirection direction, string currency,
        IEnumerable<AgreementLineVersion> lineVersions, IEnumerable<AgreementPriceVersion> priceVersions,
        DateOnly from, DateOnly to, IReadOnlyDictionary<Guid, List<AgreementAutomaticPrice>>? automaticPrices = null)
    {
        if (from > to || from == default || to.Year > 9998 || to.DayNumber - from.DayNumber > 3660 ||
            !Enum.IsDefined(direction) || !Currencies.Contains(currency))
            throw new AgreementValidationException("FinanceInvalidRange");
        var result = new List<AgreementCalculatedPeriod>();
        var published = lineVersions.Where(x => x.AgreementId == agreementId && x.Status != AgreementLineStatus.Draft).ToArray();
        var publishedSequences = published.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        // Every price version is issued with a line snapshot of the same sequence.
        // Abandoned draft dates/prices must never take effect after later activation.
        var prices = priceVersions.Where(x => x.Independent || publishedSequences.Contains((x.LineId, x.Sequence))).ToLookup(x => x.LineId);
        foreach (var line in published.GroupBy(x => x.LineId))
        {
            // A later recorded correction supersedes older definitions from its effective date,
            // including an older definition whose start was mistakenly registered too late.
            var versions = line.Where(v => !line.Any(newer => newer.Sequence > v.Sequence && newer.EffectiveFrom <= v.EffectiveFrom))
                .OrderBy(x => x.EffectiveFrom).ToArray();
            for (var i = 0; i < versions.Length; i++)
            {
                var v = versions[i];
                if (v.Status != AgreementLineStatus.Active) continue;
                var start = Max(v.StartDate, v.EffectiveFrom);
                var until = v.EndDate?.AddDays(1) ?? new DateOnly(9999, 1, 1);
                if (i + 1 < versions.Length && versions[i + 1].EffectiveFrom < until) until = versions[i + 1].EffectiveFrom;
                if (start >= until) continue;
                if (v.Frequency == AgreementFrequency.Once)
                {
                    var date = v.FirstPayableDate;
                    if (date >= start && date < until && date >= from && date <= to)
                        Add(v, date, date.AddDays(1), date, date.AddDays(1), false, true);
                    continue;
                }
                if (start > to || until <= from) continue;
                var reference = Reference(v, Max(start, from));
                while (reference.From < until && reference.From <= to)
                {
                    var actualStart = Max(start, reference.From);
                    var actualUntil = until < reference.Until ? until : reference.Until;
                    if (actualStart < actualUntil)
                    {
                        var includedUntil = actualUntil < v.FirstPayableDate ? actualUntil : v.FirstPayableDate;
                        if (actualStart < includedUntil) Add(v, reference.From, reference.Until, actualStart, includedUntil, true, false);
                        var paidStart = Max(actualStart, v.FirstPayableDate);
                        if (paidStart < actualUntil) Add(v, reference.From, reference.Until, paidStart, actualUntil, false, false);
                    }
                    if (reference.Until > to) break;
                    reference = Reference(v, reference.Until);
                }
            }
        }
        // Allocate rounding across the whole logical event before applying the overlap filter.
        var rounded = new List<AgreementCalculatedPeriod>();
        foreach (var group in result.GroupBy(x => x.EventKey))
        {
            decimal raw = 0, previous = 0;
            var payable = group.Where(x => !x.Included).ToArray();
            if (payable.Length == 0) payable = group.ToArray();
            var latest = payable.OrderByDescending(x => x.From).First();
            var timing = published.Single(x => x.Id == latest.LineVersionId).BillingTiming;
            var invoice = timing == AgreementBillingTiming.Advance ? payable.Min(x => x.From) : payable.Max(x => x.To).AddDays(1);
            foreach (var row in group.OrderBy(x => x.From).ThenBy(x => x.PriceVersionId))
            {
                raw += row.Included ? 0 : row.Quantity * row.UnitPrice * row.ActiveDays / row.ReferenceDays;
                var cumulative = decimal.Round(raw, 2, MidpointRounding.AwayFromZero);
                rounded.Add(row with { Amount = cumulative - previous, InvoiceDate = invoice }); previous = cumulative;
            }
        }
        return rounded.Where(x => x.From <= to && x.To >= from).OrderBy(x => x.From).ThenBy(x => x.LineId).ThenBy(x => x.Included).ToList();

        void Add(AgreementLineVersion v, DateOnly referenceFrom, DateOnly referenceUntil,
            DateOnly actualFrom, DateOnly actualUntil, bool included, bool once)
        {
            var invoice = v.BillingTiming == AgreementBillingTiming.Advance ? actualFrom : actualUntil;
            var projected = automaticPrices?.GetValueOrDefault(v.LineId) ?? [];
            var boundaries = prices[v.LineId].Where(x => x.EffectiveFrom > actualFrom && x.EffectiveFrom < actualUntil)
                .Select(x => x.EffectiveFrom).Concat(projected.Where(x => x.EffectiveFrom > actualFrom && x.EffectiveFrom < actualUntil).Select(x => x.EffectiveFrom))
                .Append(actualFrom).Append(actualUntil).Distinct().Order().ToArray();
            for (var part = 0; part < boundaries.Length - 1; part++)
            {
                var partFrom = boundaries[part]; var partUntil = boundaries[part + 1];
                var price = prices[v.LineId].Where(x => x.AgreementId == agreementId && x.EffectiveFrom <= partFrom)
                    .OrderByDescending(x => x.Sequence).FirstOrDefault()
                    ?? throw new AgreementValidationException("FinanceMissingPrice");
                var automatic = projected.LastOrDefault(x => x.EffectiveFrom <= partFrom && x.PriceVersionId == price.Id);
                int days = partUntil.DayNumber - partFrom.DayNumber, totalDays = referenceUntil.DayNumber - referenceFrom.DayNumber;
                result.Add(new(agreementId, v.LineId, v.Name, direction, currency, referenceFrom, referenceUntil.AddDays(-1),
                    partFrom, partUntil.AddDays(-1), invoice, price.Quantity, automatic?.UnitPrice ?? price.UnitPrice, days, totalDays, 0, included,
                    v.Id, price.Id, once ? $"{v.LineId:N}:once" : $"{v.LineId:N}:{referenceFrom:yyyyMMdd}",
                    v.Documents.Select(x => x.DocumentId).Order().ToArray()) { IndexAdjustments = automatic?.Adjustments ?? [] });
                if (result.Count > 20000) throw new AgreementValidationException("FinanceInvalidRange");
            }
        }
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
