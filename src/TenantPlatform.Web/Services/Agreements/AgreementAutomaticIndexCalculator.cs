using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public record AgreementAutomaticAdjustment(Guid IndexId, string IndexName, AgreementIndexValue BaseIndex,
    AgreementIndexValue ComparisonIndex, DateOnly EffectiveDate, decimal OldPrice, decimal NewPrice, decimal Percent);
public record AgreementAutomaticPrice(DateOnly EffectiveFrom, Guid PriceVersionId, decimal UnitPrice,
    IReadOnlyList<AgreementAutomaticAdjustment> Adjustments);

/// <summary>
/// Read-only price projection, shared by forecasts and financial bases. No price or proposal rows are created.
/// Manual prices (including historical approved adjustments) reset the price anchor; quantity changes do not.
/// </summary>
public static class AgreementAutomaticIndexCalculator
{
    public static Dictionary<Guid, List<AgreementAutomaticPrice>> Project(bool needsReview,
        IReadOnlyList<AgreementLineVersion> versions, IReadOnlyList<AgreementPriceVersion> prices,
        IReadOnlyList<AgreementIndexSelection> selections, IReadOnlyList<AgreementIndex> indices,
        IReadOnlyList<AgreementIndexValue> values)
    {
        var result = new Dictionary<Guid, List<AgreementAutomaticPrice>>();
        if (needsReview || selections.Count == 0) return result;
        var indexNames = indices.ToDictionary(x => x.Id, x => x.Name);
        var levels = values.Where(x => !x.Superseded).GroupBy(x => new { x.IndexId, x.PeriodKey })
            .Select(g => g.MaxBy(x => x.Revision)!).OrderBy(x => x.Period).ToLookup(x => x.IndexId);
        var priceLookup = prices.ToLookup(x => x.LineId);
        var indexDates = levels.SelectMany(x => x).Select(x => x.Period).Concat(selections.Select(x => x.EffectiveFrom)).Distinct().ToArray();
        foreach (var group in versions.GroupBy(x => x.LineId))
        {
            if (!group.Any(x => x.IndexRegulated)) continue;
            var lineVersions = group.OrderByDescending(x => x.Sequence).ToArray();
            var linePrices = priceLookup[group.Key].OrderByDescending(x => x.Sequence).ToArray();
            var dates = indexDates.Concat(lineVersions.SelectMany(x => new[] { x.EffectiveFrom, x.StartDate }))
                .Concat(linePrices.Select(x => x.EffectiveFrom)).Distinct().Order().ToArray();
            var projected = new List<AgreementAutomaticPrice>();
            var adjustments = new List<AgreementAutomaticAdjustment>();
            AgreementPriceVersion? previousPrice = null;
            AgreementIndexValue? basis = null;
            Guid? previousIndex = null;
            decimal amount = 0;
            bool wasEligible = false;
            foreach (var date in dates)
            {
                var line = lineVersions.FirstOrDefault(x => x.EffectiveFrom <= date);
                var price = linePrices.FirstOrDefault(x => x.EffectiveFrom <= date);
                if (line is null || price is null) continue;
                var index = selections.Where(x => x.EffectiveFrom <= date).MaxBy(x => x.Sequence)?.IndexId;
                var eligible = line.IndexRegulated && line.Status == AgreementLineStatus.Active && date >= line.StartDate && !(date > line.EndDate) && index.HasValue;
                var resetPrice = previousPrice is null || price.UnitPrice != previousPrice.UnitPrice ||
                    (price.IndexBaseDate ?? price.EffectiveFrom) != (previousPrice.IndexBaseDate ?? previousPrice.EffectiveFrom);
                var level = index.HasValue ? levels[index.Value].LastOrDefault(x => x.Period <= date) : null;
                if (resetPrice)
                {
                    amount = price.UnitPrice;
                    adjustments.Clear();
                }
                if (!eligible) basis = null;
                else if (resetPrice || !wasEligible || previousIndex != index || basis is null)
                {
                    // Registered price is the price at its effective date. Enabling or switching indices
                    // starts from the current level, without catching up for a disabled interval.
                    basis = level;
                }
                else if (level is not null && level.PeriodKey != basis.PeriodKey)
                {
                    var change = AgreementAdjustmentCalculator.Calculate(amount, basis.Value, level.Value);
                    adjustments.Add(new(index!.Value, indexNames[index.Value], basis, level, date, amount, change.Price, change.AppliedPercent));
                    amount = change.Price;
                    basis = level;
                }
                // Index switches or unrelated register dates that leave the price/evidence unchanged
                // must not split an existing billing period or alter its rounding.
                var last = projected.LastOrDefault();
                if (last is null || last.PriceVersionId != price.Id || last.UnitPrice != amount || !last.Adjustments.SequenceEqual(adjustments))
                    projected.Add(new(date, price.Id, amount, adjustments.ToArray()));
                previousPrice = price; previousIndex = index; wasEligible = eligible;
            }
            result[group.Key] = projected;
        }
        return result;
    }
}
