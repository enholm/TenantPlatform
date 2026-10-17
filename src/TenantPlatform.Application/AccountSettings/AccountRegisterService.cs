using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Application.AccountSettings;

public enum AccountRegisterKind { OrganizationElements, GeographicAreas }
public sealed record AccountRegisterEntry(Guid Id, Guid? ParentId, string Name, string? Description, bool IsActive);

// A register-specific transaction: the adapter locks the account before exposing state.
// Rules run in Application while the same lock remains held through commit.
public interface IAccountRegisterTransaction : IAsyncDisposable
{
    IReadOnlyList<AccountRegisterEntry> Entries { get; }
    Task<bool> IsReferencedAsync(Guid id, CancellationToken ct);
    Task SaveAsync(AccountRegisterEntry entry, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IAccountRegisterData
{
    Task<IReadOnlyList<AccountRegisterEntry>> ReadAsync(Guid accountId, AccountRegisterKind kind, CancellationToken ct);
    Task<IAccountRegisterTransaction> BeginAsync(Guid accountId, AccountRegisterKind kind, CancellationToken ct);
}

public sealed class AccountRegisterService(ICurrentActor actor, IAuthorizationService authorization, IAccountRegisterData data)
{
    private async Task<ActorSnapshot> RequireAsync(CancellationToken ct)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId is not Guid account ||
            !await authorization.CanAsync(Permissions.AccountStructure.Manage, new(ResourceKind.Account, AccountId: account), ct))
            throw new UnauthorizedAccessException();
        return current;
    }

    public async Task<List<OrganizationElement>> ReadOrganizationElementsAsync(CancellationToken ct = default)
    {
        var current = await RequireAsync(ct);
        var account = current.AccountId!.Value;
        return (await data.ReadAsync(account, AccountRegisterKind.OrganizationElements, ct))
            .Select(x => new OrganizationElement { Id = x.Id, AccountId = account, ParentId = x.ParentId,
                Name = x.Name, Description = x.Description, IsActive = x.IsActive }).ToList();
    }

    public async Task<List<GeographicArea>> ReadGeographicAreasAsync(CancellationToken ct = default)
    {
        var current = await RequireAsync(ct);
        var account = current.AccountId!.Value;
        return (await data.ReadAsync(account, AccountRegisterKind.GeographicAreas, ct))
            .Select(x => new GeographicArea { Id = x.Id, AccountId = account, ParentId = x.ParentId,
                Name = x.Name, Description = x.Description, IsActive = x.IsActive }).ToList();
    }

    public async Task SaveAsync(AccountRegisterKind kind, Guid? id, AccountRegisterInput input, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        // Snapshot mutable input before authorization awaits I/O.
        var entry = new AccountRegisterEntry(id ?? Guid.NewGuid(), input.ParentId, input.Name?.Trim() ?? "",
            string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), input.IsActive);
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 200 || entry.Description?.Length > 2000)
            throw new ArgumentException("AccountRegisterInvalidInput");
        var current = await RequireAsync(ct);
        var account = current.AccountId!.Value;
        await using var transaction = await data.BeginAsync(account, kind, ct);
        await RevalidateAsync(current, ct);
        var parents = transaction.Entries.ToDictionary(x => x.Id, x => x.ParentId);
        var parent = entry.ParentId;
        var visited = new HashSet<Guid>();
        while (parent.HasValue)
        {
            if (parent == id || !visited.Add(parent.Value)) throw new AccountHierarchyException("AccountHierarchyCycle");
            if (!parents.TryGetValue(parent.Value, out parent)) throw new AccountHierarchyException("AccountHierarchyInvalidParent");
        }
        if (id.HasValue && !parents.ContainsKey(id.Value)) throw new InvalidOperationException("AccountRegisterNotFound");
        await transaction.SaveAsync(entry, ct);
    }

    public async Task DeleteAsync(AccountRegisterKind kind, Guid id, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var current = await RequireAsync(ct);
        var account = current.AccountId!.Value;
        await using var transaction = await data.BeginAsync(account, kind, ct);
        await RevalidateAsync(current, ct);
        if (!transaction.Entries.Any(x => x.Id == id)) throw new InvalidOperationException("AccountRegisterNotFound");
        if (transaction.Entries.Any(x => x.ParentId == id)) throw new AccountHierarchyException("AccountHierarchyHasChildren");
        if (await transaction.IsReferencedAsync(id, ct)) throw new AccountHierarchyException("AccountHierarchyUsedByAgreement");
        await transaction.DeleteAsync(id, ct);
    }

    private async Task RevalidateAsync(ActorSnapshot expected, CancellationToken ct)
    {
        if (await RequireAsync(ct) != expected) throw new UnauthorizedAccessException();
    }
}
