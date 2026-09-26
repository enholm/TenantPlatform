using TenantPlatform.Core.Dimensions;

namespace TenantPlatform.Web.Services.Dimensions;

public sealed record DimensionValueOption(Guid Id, Guid DimensionId, Guid? ParentId, string Name, string Code,
    string Path, int Depth, bool IsActive, bool Selectable);
public static class DimensionHierarchy
{
    // Build returns a depth-first tree. Exclude the edited node's entire subtree,
    // including inactive descendants, so the parent picker cannot propose a cycle.
    public static IEnumerable<DimensionValueOption> AllowedParents(IEnumerable<DimensionValueOption> hierarchy, Guid? editingId)
    {
        int? excludedDepth = null;
        foreach (var row in hierarchy)
        {
            if (row.Id == editingId) { excludedDepth = row.Depth; continue; }
            if (excludedDepth.HasValue && row.Depth > excludedDepth.Value) continue;
            excludedDepth = null;
            yield return row;
        }
    }

    public static List<DimensionValueOption> Build(Dimension dimension, IEnumerable<DimensionValue> source)
    {
        var values = source.Where(x => x.DimensionId == dimension.Id && x.AccountId == dimension.AccountId).ToList();
        var children = values.ToLookup(x => x.ParentId);
        var result = new List<DimensionValueOption>();
        var pending = new Stack<(DimensionValue Value, string Path, int Depth, bool Active)>();
        void Push(Guid? parent, string prefix, int depth, bool active)
        {
            foreach (var value in children[parent].OrderByDescending(x => x.SortOrder).ThenByDescending(x => x.Name).ThenByDescending(x => x.Id))
                pending.Push((value, string.IsNullOrEmpty(prefix) ? value.Name : prefix + " / " + value.Name, depth, active && value.IsActive));
        }
        Push(null, "", 0, dimension.IsActive);
        var visited = new HashSet<Guid>();
        while (pending.TryPop(out var row))
        {
            if (!visited.Add(row.Value.Id)) throw new DimensionValidationException("DimensionCycle");
            result.Add(new(row.Value.Id, dimension.Id, row.Value.ParentId, row.Value.Name, row.Value.Code,
                row.Path, row.Depth, row.Active, row.Active && (!dimension.LeafOnly || !children[row.Value.Id].Any())));
            Push(row.Value.Id, row.Path, row.Depth + 1, row.Active);
        }
        if (result.Count != values.Count) throw new DimensionValidationException("DimensionInvalidParent");
        return result;
    }
}
public sealed class DimensionValidationException(string key) : Exception(key);
