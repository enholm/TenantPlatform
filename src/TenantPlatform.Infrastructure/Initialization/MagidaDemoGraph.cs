using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Infrastructure.Initialization;

// Candidate or insert-only batch. Existing rows are never modified by reconciliation.
public sealed class MagidaDemoGraph
{
    public List<Dimension> Dimensions { get; init; } = [];
    public List<DimensionTranslation> Translations { get; init; } = [];
    public List<DimensionValue> Values { get; init; } = [];
    public List<AgreementDimensionRule> Rules { get; init; } = [];
    public List<Organization> Counterparties { get; init; } = [];
    public List<AgreementIndex> Indices { get; init; } = [];
    public List<AgreementIndexValue> IndexValues { get; init; } = [];
    public List<Agreement> Agreements { get; init; } = [];
    public List<AgreementDimensionAssignment> Assignments { get; init; } = [];
    public List<AgreementIndexSelection> IndexSelections { get; init; } = [];
    public List<AgreementLine> Lines { get; init; } = [];
    public List<AgreementLineVersion> LineVersions { get; init; } = [];
    public List<AgreementPriceVersion> PriceVersions { get; init; } = [];

    public MagidaDemoGraph MissingFrom(MagidaDemoGraph existing)
    {
        RequireAccount(); existing.RequireAccount();
        var dimensions = Missing(Dimensions, existing.Dimensions, x => x.Id);
        var values = Missing(Values, existing.Values, x => x.Id);
        // Do not silently adopt another dimension/value or overwrite user configuration on code collision.
        foreach (var d in dimensions)
            if (existing.Dimensions.Any(x => x.Code == d.Code))
                throw new InvalidOperationException($"Magida dimension code {d.Code} already belongs to another ID.");
        foreach (var v in values)
            if (existing.Values.Any(x => x.DimensionId == v.DimensionId && x.Code == v.Code))
                throw new InvalidOperationException($"Magida value code {v.Code} in {v.DimensionId} already belongs to another ID.");

        var agreements = Missing(Agreements, existing.Agreements, x => x.Id);
        var newAgreementIds = agreements.Select(x => x.Id).ToHashSet();
        var languages = existing.Translations.Select(x => (x.DimensionId, x.LanguageCode)).ToHashSet();
        return new()
        {
            Dimensions = dimensions,
            Translations = Missing(Translations, existing.Translations, x => x.Id)
                .Where(x => !languages.Contains((x.DimensionId, x.LanguageCode))).ToList(),
            Values = values,
            Rules = Missing(Rules, existing.Rules, x => x.DimensionId),
            Counterparties = Missing(Counterparties, existing.Counterparties, x => x.Id),
            Indices = Missing(Indices, existing.Indices, x => x.Id),
            IndexValues = Missing(IndexValues, existing.IndexValues, x => x.Id),
            Agreements = agreements,
            // Preserve the established whole-agreement skip, including edited/explicitly cleared classifications.
            // A missing child on an existing agreement is intentionally not repaired.
            Assignments = Assignments.Where(x => newAgreementIds.Contains(x.AgreementId)).ToList(),
            IndexSelections = IndexSelections.Where(x => newAgreementIds.Contains(x.AgreementId)).ToList(),
            Lines = Lines.Where(x => newAgreementIds.Contains(x.AgreementId)).ToList(),
            LineVersions = LineVersions.Where(x => newAgreementIds.Contains(x.AgreementId)).ToList(),
            PriceVersions = PriceVersions.Where(x => newAgreementIds.Contains(x.AgreementId)).ToList()
        };
    }

    private static List<T> Missing<T>(IEnumerable<T> candidates, IEnumerable<T> existing, Func<T, Guid> id)
    {
        var ids = existing.Select(id).ToHashSet();
        return candidates.Where(x => !ids.Contains(id(x))).ToList();
    }

    private void RequireAccount()
    {
        var accounts = Dimensions.Select(x => x.AccountId).Concat(Translations.Select(x => x.AccountId))
            .Concat(Values.Select(x => x.AccountId)).Concat(Rules.Select(x => x.AccountId))
            .Concat(Counterparties.Select(x => x.AccountId)).Concat(Indices.Select(x => x.AccountId))
            .Concat(IndexValues.Select(x => x.AccountId)).Concat(Agreements.Select(x => x.AccountId))
            .Concat(Assignments.Select(x => x.AccountId)).Concat(IndexSelections.Select(x => x.AccountId))
            .Concat(Lines.Select(x => x.AccountId)).Concat(LineVersions.Select(x => x.AccountId))
            .Concat(PriceVersions.Select(x => x.AccountId));
        if (accounts.Any(x => x != SeedIds.MagidaAccount))
            throw new InvalidOperationException("The Magida demo graph contains data from another account.");
    }
}
