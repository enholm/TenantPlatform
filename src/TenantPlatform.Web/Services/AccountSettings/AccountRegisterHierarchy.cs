namespace TenantPlatform.Web.Services.AccountSettings;

public sealed record AccountHierarchyRow(Guid Id, int Depth, string Path, IReadOnlyList<Guid> Ancestors);

public static class AccountRegisterHierarchy
{
    public static IReadOnlyList<AccountHierarchyRow> Build(IEnumerable<(Guid Id, Guid? ParentId, string Name)> source)
    {
        var items = source.ToList();
        var children = items.ToLookup(x => x.ParentId);
        var result = new List<AccountHierarchyRow>();
        var pending = new Stack<(Guid Id, string Name, string Path, Guid[] Ancestors)>();
        foreach (var root in children[null].OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            pending.Push((root.Id, root.Name, root.Name, []));
        var visited = new HashSet<Guid>();
        while (pending.TryPop(out var row))
        {
            if (!visited.Add(row.Id)) continue;
            result.Add(new(row.Id, row.Ancestors.Length, row.Path, row.Ancestors));
            foreach (var child in children[row.Id].OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                pending.Push((child.Id, child.Name, row.Path + " / " + child.Name, [..row.Ancestors, row.Id]));
        }
        return result;
    }
}
