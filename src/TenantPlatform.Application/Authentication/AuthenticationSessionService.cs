using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Localization;

namespace TenantPlatform.Application.Authentication;

public sealed record AuthenticationSessionSnapshot(User User, Guid? LastAccountId, IReadOnlyList<Guid> AccountIds, IReadOnlyList<RoleAssignment> Assignments);
public sealed record AuthenticationSession(User? User, Guid? AccountId, IReadOnlyCollection<UserRole> Roles, string? Error = null, bool SelectAccountRequired = false);
public interface IAuthenticationSessionData
{
    Task<AuthenticationSessionSnapshot?> ReadAsync(Guid userId, CancellationToken ct);
    Task RememberAccountAsync(Guid userId, Guid accountId, CancellationToken ct);
    Task SaveLanguageAsync(Guid userId, string language, CancellationToken ct);
}

public sealed class AuthenticationSessionService(ILocalAuthenticationService credentials, IAuthenticationSessionData data,
    ICurrentActor actor, IAuthorizationData authorization)
{
    public async Task<AuthenticationSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // Identity is established only by the trusted credential adapter's verified result.
        var result = await credentials.AuthenticateAsync(email, password, ct);
        if (!result.Succeeded || result.User is not { IsActive: true } verified) return new(null, null, [], "invalid-login");
        var state = await data.ReadAsync(verified.Id, ct);
        if (state is null || !state.User.IsActive) return new(null, null, [], "invalid-login");
        if (state.AccountIds.Count == 0)
            return state.User.IsPlatformAdmin ? new(state.User, null, []) : new(null, null, [], "no-account-access");
        Guid? selected = state.AccountIds.Count == 1 ? state.AccountIds[0] : state.LastAccountId is Guid last && state.AccountIds.Contains(last) ? last : null;
        if (state.AccountIds.Count == 1) await data.RememberAccountAsync(state.User.Id, selected!.Value, ct);
        return new(state.User, selected, state.Assignments.Where(x => x.AccountId == selected).Select(x => x.Role).Distinct().ToArray(), SelectAccountRequired: !selected.HasValue);
    }
    public async Task<AuthenticationSession> SelectAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) throw new UnauthorizedAccessException();
        var subject = await authorization.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        await data.RememberAccountAsync(current.UserId, accountId, ct);
        var state = await data.ReadAsync(current.UserId, ct);
        if (state is null || !state.User.IsActive || !state.AccountIds.Contains(accountId)) throw new UnauthorizedAccessException();
        return new(state.User, accountId, state.Assignments.Where(x => x.AccountId == accountId).Select(x => x.Role).Distinct().ToArray());
    }
    public async Task<string> ChangeLanguageAsync(string language, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) throw new UnauthorizedAccessException();
        var canonical = SupportedLanguages.All.SingleOrDefault(x => string.Equals(x.Code, language, StringComparison.OrdinalIgnoreCase))?.Code
            ?? throw new ArgumentException("Unsupported language.", nameof(language));
        var subject = await authorization.GetSubjectAsync(current.UserId, null, ct);
        if (!subject.IsActiveUser) throw new UnauthorizedAccessException();
        await data.SaveLanguageAsync(current.UserId, canonical, ct);
        return canonical;
    }
}
