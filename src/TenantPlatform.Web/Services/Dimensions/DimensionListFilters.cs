using TenantPlatform.Core.Dimensions;

namespace TenantPlatform.Web.Services.Dimensions;

public sealed class DimensionListFilterSelection
{
    public Guid DimensionId { get; set; }
    public Guid? ValueId { get; set; }
    // Compatibility input from the former multi-select contract. Plan normalizes
    // this to the first value in catalog order; new UI never populates it.
    public List<Guid> ValueIds { get; set; } = [];
    public bool IncludeUnspecified { get; set; }
}

public sealed record DimensionListFilterValue(Guid Id, Guid? ParentId, string Name, string Code, string Path, int Depth, bool IsHistorical);
public sealed record DimensionListFilterOption(Guid DimensionId, string Code, string Name, string? Description,
    bool IsHistorical, bool AllowMultiple, List<DimensionListFilterValue> Values);
public sealed record DimensionListFilterCatalog(List<DimensionListFilterOption> Dimensions);
public sealed record DimensionListFilterCriterion(Guid DimensionId, Guid[] MatchingValueIds, bool IncludeUnspecified);

public static class DimensionListFilters
{
    public static DimensionListFilterCatalog Catalog(IEnumerable<Dimension> dimensions, IEnumerable<DimensionValue> values,
        IReadOnlySet<Guid> enabledDimensions, IReadOnlySet<(Guid DimensionId, Guid ValueId)> used,
        string requestedLanguage, string? defaultLanguage)
    {
        var allValues = values.ToList();
        var options = new List<DimensionListFilterOption>();
        foreach (var dimension in dimensions.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id))
        {
            var normal = dimension.IsActive && enabledDimensions.Contains(dimension.Id);
            var usedIds = used.Where(x => x.DimensionId == dimension.Id).Select(x => x.ValueId).ToHashSet();
            if (!normal && usedIds.Count == 0) continue;
            var paths = DimensionHierarchy.Build(dimension, allValues);
            var included = normal ? paths.Where(x => x.IsActive).Select(x => x.Id).ToHashSet() : new HashSet<Guid>();
            foreach (var id in usedIds)
            {
                var current = paths.SingleOrDefault(x => x.Id == id);
                while (current is not null && included.Add(current.Id) && current.ParentId.HasValue)
                    current = paths.SingleOrDefault(x => x.Id == current.ParentId.Value);
            }
            options.Add(new(dimension.Id, dimension.Code,
                DimensionLocalization.Name(dimension, requestedLanguage, defaultLanguage),
                DimensionLocalization.Description(dimension, requestedLanguage, defaultLanguage), !normal, dimension.AllowMultiple,
                paths.Where(x => included.Contains(x.Id)).Select(x => new DimensionListFilterValue(
                    x.Id, x.ParentId, x.Name, x.Code, x.Path, x.Depth, !normal || !x.IsActive)).ToList()));
        }
        return new(options);
    }

    public static List<DimensionListFilterCriterion> Plan(DimensionListFilterCatalog catalog,
        IEnumerable<DimensionListFilterSelection>? selections)
    {
        var result = new List<DimensionListFilterCriterion>();
        var seen = new HashSet<Guid>();
        foreach (var selection in selections ?? [])
        {
            if (selection is null || selection.DimensionId == Guid.Empty || !seen.Add(selection.DimensionId) ||
                selection.ValueIds is null || selection.ValueIds.Contains(Guid.Empty) ||
                selection.ValueIds.Distinct().Count() != selection.ValueIds.Count || selection.ValueId == Guid.Empty)
                throw new DimensionValidationException("DimensionFilterInvalid");
            var dimension = catalog.Dimensions.SingleOrDefault(x => x.DimensionId == selection.DimensionId)
                ?? throw new DimensionValidationException("DimensionFilterInvalid");
            var legacy = selection.ValueIds.ToHashSet();
            if (legacy.Any(id => dimension.Values.All(x => x.Id != id)) ||
                selection.ValueId.HasValue && dimension.Values.All(x => x.Id != selection.ValueId.Value))
                throw new DimensionValidationException("DimensionFilterInvalid");
            if (selection.IncludeUnspecified)
            {
                selection.ValueId = null; selection.ValueIds.Clear();
                result.Add(new(dimension.DimensionId, [], true));
                continue;
            }
            var selected = selection.ValueId ?? dimension.Values.FirstOrDefault(x => legacy.Contains(x.Id))?.Id;
            if (!selected.HasValue) continue;
            selection.ValueId = selected; selection.ValueIds.Clear();
            var matching = dimension.Values.Where(value => IsSelfOrDescendant(value.Id, selected.Value, dimension.Values))
                .Select(x => x.Id).Distinct().ToArray();
            result.Add(new(dimension.DimensionId, matching, false));
        }
        return result;
    }

    public static bool Matches(IReadOnlyCollection<(Guid DimensionId, Guid ValueId)> assignments,
        IReadOnlyCollection<DimensionListFilterCriterion> criteria) => criteria.All(criterion =>
        (criterion.IncludeUnspecified && assignments.All(x => x.DimensionId != criterion.DimensionId)) ||
        assignments.Any(x => x.DimensionId == criterion.DimensionId && criterion.MatchingValueIds.Contains(x.ValueId)));

    private static bool IsSelfOrDescendant(Guid candidate, Guid parent, IReadOnlyCollection<DimensionListFilterValue> values)
    {
        var current = values.SingleOrDefault(x => x.Id == candidate);
        while (current is not null)
        {
            if (current.Id == parent) return true;
            current = current.ParentId.HasValue ? values.SingleOrDefault(x => x.Id == current.ParentId.Value) : null;
        }
        return false;
    }
}
