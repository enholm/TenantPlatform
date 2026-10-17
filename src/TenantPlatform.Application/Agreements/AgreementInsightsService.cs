using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public enum AgreementInsightDimension { Type, Counterparty, Geography, Organization, Status, Owner }
public sealed record AgreementInsightGroup(string Key, string? Name, AgreementType? Type, int Count, bool Direct = false, AgreementStatus? Status = null);
public sealed record AgreementInsightsDto(IReadOnlyList<AgreementInsightGroup> Groups, IReadOnlyList<AccountHierarchyRow> Options)
{
    public int Total => Groups.Sum(x => x.Count);
}

public sealed record AgreementHierarchyNode(Guid Id, Guid? ParentId, string Name);
public sealed record AgreementHierarchyCount(Guid? NodeId, int Count);
public sealed record AgreementHierarchyData(IReadOnlyList<AgreementHierarchyNode> Nodes, IReadOnlyList<AgreementHierarchyCount> Counts);

public interface IAgreementInsightsData
{
    Task<IReadOnlyList<AgreementInsightGroup>> GetGroupsAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct);
    Task<AgreementHierarchyData> GetHierarchyAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct);
}

public interface IAgreementInsightsService
{
    Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightDimension dimension, Guid? selected = null, CancellationToken ct = default);
}

public sealed class AgreementInsightsService(IAuthorizationService authorization, IAgreementInsightsData data) : IAgreementInsightsService
{
    public async Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightDimension dimension, Guid? selected = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
        // Value parameters are immutable; authorization captures the actor before its first await.
        var hierarchical = dimension is AgreementInsightDimension.Geography or AgreementInsightDimension.Organization;
        if (!hierarchical && selected.HasValue) throw new ArgumentException("A selected node requires a hierarchy dimension.", nameof(selected));
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        if (!hierarchical) return new(await data.GetGroupsAsync(scope, dimension, ct), []);
        var hierarchy = await data.GetHierarchyAsync(scope, dimension, ct);
        var rows = AccountRegisterHierarchy.Build(hierarchy.Nodes.Select(x => (x.Id, x.ParentId, x.Name)));
        if (!scope.CanReadAll)
        {
            var assigned = hierarchy.Counts.Where(x => x.Count > 0).Select(x => x.NodeId).OfType<Guid>().ToHashSet();
            var allowed = rows.Where(x => assigned.Contains(x.Id)).SelectMany(x => x.Ancestors.Append(x.Id)).ToHashSet();
            rows = rows.Where(x => allowed.Contains(x.Id)).ToList();
        }
        return new(AgreementInsightRules.GroupHierarchy(hierarchy.Counts, rows, selected), rows);
    }
}

public static class AgreementInsightRules
{
    public static IReadOnlyList<AgreementInsightGroup> GroupHierarchy(IEnumerable<AgreementHierarchyCount> assignments,
        IReadOnlyList<AccountHierarchyRow> rows, Guid? selected)
    {
        var byId = rows.ToDictionary(x => x.Id);
        if (selected.HasValue && !byId.ContainsKey(selected.Value)) throw new UnauthorizedAccessException();
        var keys = new List<(Guid? Id, bool Direct, int Count)>();
        foreach (var assignment in assignments)
        {
            var id = assignment.NodeId;
            if (!id.HasValue || !byId.TryGetValue(id.Value, out var row))
            {
                if (!selected.HasValue) keys.Add((null, false, assignment.Count));
                continue;
            }
            if (!selected.HasValue) keys.Add((row.Ancestors.FirstOrDefault(row.Id), false, assignment.Count));
            else if (row.Id == selected) keys.Add((row.Id, true, assignment.Count));
            else
            {
                var path = row.Ancestors.Append(row.Id).ToList();
                var parentIndex = path.IndexOf(selected.Value);
                if (parentIndex >= 0) keys.Add((path[parentIndex + 1], false, assignment.Count));
            }
        }
        return keys.GroupBy(x => (x.Id, x.Direct)).Select(g => new AgreementInsightGroup(
            g.Key.Id?.ToString() ?? "missing", g.Key.Id.HasValue ? byId[g.Key.Id.Value].Path : null,
            null, g.Sum(x => x.Count), g.Key.Direct)).OrderByDescending(x => x.Count).ThenBy(x => x.Name).ThenBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Direct).ToList();
    }
}
