namespace TenantPlatform.Application.Security;

public sealed record MemberAccount(Guid Id, string Name);
public interface IAccountContextData
{
    Task<IReadOnlyList<MemberAccount>> ReadMemberAccountsAsync(Guid userId, CancellationToken ct);
}

// This operation intentionally does not require an already-selected account.
// Only the trusted actor's own memberships can be listed.
public sealed class AccountContextReader(ICurrentActor actor, IAuthorizationData authorization, IAccountContextData data)
{
    public async Task<IReadOnlyList<MemberAccount>> ReadAsync(CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) throw new UnauthorizedAccessException();
        var subject = await authorization.GetSubjectAsync(current.UserId, null, ct);
        if (!subject.IsActiveUser) throw new UnauthorizedAccessException();
        return await data.ReadMemberAccountsAsync(current.UserId, ct);
    }

    public async Task<string?> ReadSelectedNameAsync(CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) throw new UnauthorizedAccessException();
        if (current.AccountId is null) return null;
        var subject = await authorization.GetSubjectAsync(current.UserId, current.AccountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        return (await data.ReadMemberAccountsAsync(current.UserId, ct)).SingleOrDefault(x => x.Id == current.AccountId)?.Name;
    }
}
