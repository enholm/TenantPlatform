using TenantPlatform.Core.Dimensions;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Agreements;

public sealed record AgreementInsightAssignment(Guid AgreementId, Guid ValueId);

public static class AgreementInsightHierarchy
{
    // Inputs are projected from the authorized agreement query. No EF, recursion, or persisted ancestor assignments.
    public static AgreementInsightsDto Aggregate(Dimension dimension, IEnumerable<DimensionValue> values,
        IEnumerable<Guid> visibleAgreementIds, IEnumerable<AgreementInsightAssignment> assignments, Guid? selected, bool administrator)
    {
        List<DimensionValueOption> hierarchy;
        try { hierarchy = DimensionHierarchy.Build(dimension, values); }
        catch (DimensionValidationException) { throw new AgreementValidationException("DashboardInvalidHierarchy"); }
        var nodes = hierarchy.ToDictionary(x => x.Id);
        var visible = visibleAgreementIds.ToHashSet();
        var direct = new Dictionary<Guid, HashSet<Guid>>();
        var rollup = new Dictionary<Guid, HashSet<Guid>>();
        var assigned = new HashSet<Guid>();
        foreach (var assignment in assignments.Distinct())
        {
            if (!visible.Contains(assignment.AgreementId) || !nodes.ContainsKey(assignment.ValueId))
                throw new AgreementValidationException("DashboardInvalidHierarchy");
            assigned.Add(assignment.AgreementId);
            Add(direct, assignment.ValueId, assignment.AgreementId);
            // DimensionHierarchy already validated the complete parent graph, including inactive nodes.
            Guid? current = assignment.ValueId;
            while (current.HasValue)
            {
                Add(rollup, current.Value, assignment.AgreementId);
                current = nodes[current.Value].ParentId;
            }
        }
        var allowed = administrator ? nodes.Keys.ToHashSet() : rollup.Keys.ToHashSet();
        if (selected.HasValue && !allowed.Contains(selected.Value)) throw new UnauthorizedAccessException();
        var options = hierarchy.Where(x => allowed.Contains(x.Id))
            .Select(x => new AgreementInsightNode(x.Id, x.ParentId, x.Path, x.Depth, x.IsActive)).ToList();
        var groups = hierarchy.Where(x => x.ParentId == selected && allowed.Contains(x.Id))
            .Select(x => new AgreementInsightGroup("node:" + x.Id, x.Path, null, Count(rollup, x.Id),
                Kind: AgreementInsightGroupKind.Node, NodeId: x.Id)).ToList();
        if (selected.HasValue)
        {
            var count = Count(direct, selected.Value);
            if (count > 0) groups.Add(new("direct:" + selected.Value, nodes[selected.Value].Path, null, count, Kind: AgreementInsightGroupKind.Direct));
        }
        else if (visible.Count > assigned.Count)
            groups.Add(new("missing", null, null, visible.Count - assigned.Count, Kind: AgreementInsightGroupKind.Unspecified));
        return new(groups.OrderByDescending(x => x.Count).ThenBy(x => x.Name).ThenBy(x => x.Key).ToList(), options,
            visible.Count, selected.HasValue ? Count(rollup, selected.Value) : visible.Count, selected, dimension.AllowMultiple);
    }

    private static void Add(Dictionary<Guid, HashSet<Guid>> sets, Guid node, Guid agreement)
    {
        if (!sets.TryGetValue(node, out var set)) sets[node] = set = [];
        set.Add(agreement);
    }
    private static int Count(Dictionary<Guid, HashSet<Guid>> sets, Guid id) => sets.TryGetValue(id, out var set) ? set.Count : 0;
}
