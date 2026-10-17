using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.UserAdministration;

public interface IUserPasswordHasher
{
    string HashPassword(LoginAccount account, string password);
}

public interface IUserAdministrationData
{
    Task<List<UserListItemDto>> ReadUsersAsync(UserAdministrationScope scope, CancellationToken ct);
    Task<UserDetailsDto?> ReadUserAsync(UserAdministrationScope scope, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<UserAccountAccessDto>> ReadAccessAsync(UserAdministrationScope scope, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<UserAccountOptionDto>> ReadAccountsAsync(UserAdministrationScope scope, CancellationToken ct);
    Task<IReadOnlyList<UserOrganizationOptionDto>> ReadOrganizationsAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<UserBuildingOptionDto>> ReadBuildingsAsync(Guid accountId, CancellationToken ct);
    Task<Guid?> ReadRoleAccountAsync(UserAdministrationScope scope, Guid userId, Guid roleId, CancellationToken ct);
    Task<IUserAdministrationTransaction> BeginUserAsync(UserAdministrationScope scope, Guid? userId, Guid? createAccountId, CancellationToken ct);
    Task<IUserAdministrationTransaction> BeginAccessAsync(UserAdministrationScope scope, Guid userId, Guid accountId, CancellationToken ct);
}

public interface IUserAdministrationTransaction : IAsyncDisposable
{
    User User { get; }
    UserAccount? Membership { get; }
    IReadOnlyList<UserAccountRole> Roles { get; }
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);
    Task<bool> BuildingExistsAsync(Guid id, CancellationToken ct);
    Task<bool> OrganizationExistsAsync(Guid id, CancellationToken ct);
    void AddLogin(LoginAccount login);
    void AddRole(UserAccountRole role);
    void RemoveRole(UserAccountRole role, bool removeMembership);
    Task CommitAsync(CancellationToken ct);
}

public sealed class UserAdministrationService(IAuthorizationService authorization, IUserAdministrationData data,
    IUserPasswordHasher passwords, TimeProvider clock) : IUserAdministrationService
{
    private static void RequireAccount(UserAdministrationScope scope, Guid account)
    {
        if (!scope.IsPlatformAdmin && scope.AccountId != account) throw new UnauthorizedAccessException();
    }
    private async Task Revalidate(UserAdministrationScope expected, CancellationToken ct)
    {
        if (await authorization.GetUserAdministrationScopeAsync(ct) != expected) throw new UnauthorizedAccessException();
    }
    public async Task<List<UserListItemDto>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await data.ReadUsersAsync(await authorization.GetUserAdministrationScopeAsync(cancellationToken), cancellationToken);
    public async Task<UserDetailsDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await data.ReadUserAsync(await authorization.GetUserAdministrationScopeAsync(cancellationToken), userId, cancellationToken);
    public async Task<IReadOnlyList<UserAccountAccessDto>> GetUserAccessAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await data.ReadAccessAsync(await authorization.GetUserAdministrationScopeAsync(cancellationToken), userId, cancellationToken);
    public async Task<IReadOnlyList<UserAccountOptionDto>> GetAvailableAccountsAsync(CancellationToken cancellationToken = default) =>
        await data.ReadAccountsAsync(await authorization.GetUserAdministrationScopeAsync(cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<UserOrganizationOptionDto>> GetAvailableOrganizationsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        RequireAccount(scope, accountId);
        return await data.ReadOrganizationsAsync(accountId, cancellationToken);
    }
    public async Task<IReadOnlyList<UserBuildingOptionDto>> GetAvailableBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        RequireAccount(scope, accountId);
        return await data.ReadBuildingsAsync(accountId, cancellationToken);
    }
    public async Task<Guid> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        var account = scope.IsPlatformAdmin ? request.AccountId : scope.AccountId;
        UserAdministrationRules.ValidateUserRequest(request.FirstName, request.LastName, request.Email, request.PreferredLanguage);
        if (request.CreateLocalLogin && string.IsNullOrWhiteSpace(request.TemporaryPassword))
            throw new InvalidOperationException("TemporaryPasswordRequired");
        await using var transaction = await data.BeginUserAsync(scope, null, account, cancellationToken);
        await Revalidate(scope, cancellationToken);
        if (await transaction.EmailExistsAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken))
            throw new InvalidOperationException("UserEmailAlreadyExists");
        Apply(transaction.User, request.FirstName, request.LastName, request.Email, request.PreferredLanguage, request.IsActive);
        // Existing creation rule: even PlatformAdmin creates an ordinary user first.
        transaction.User.IsPlatformAdmin = false;
        if (request.CreateLocalLogin)
        {
            var login = new LoginAccount { Id = Guid.NewGuid(), UserId = transaction.User.Id, Email = transaction.User.Email,
                IsEnabled = request.IsLocalLoginEnabled, FailedLoginCount = 0, CreatedUtc = clock.GetUtcNow() };
            login.PasswordHash = passwords.HashPassword(login, request.TemporaryPassword!);
            transaction.AddLogin(login);
        }
        await transaction.CommitAsync(cancellationToken);
        return transaction.User.Id;
    }
    public async Task UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        UserAdministrationRules.ValidateUserRequest(request.FirstName, request.LastName, request.Email, request.PreferredLanguage);
        await using var transaction = await data.BeginUserAsync(scope, userId, null, cancellationToken);
        await Revalidate(scope, cancellationToken);
        if (await transaction.EmailExistsAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken))
            throw new InvalidOperationException("UserEmailAlreadyExists");
        Apply(transaction.User, request.FirstName, request.LastName, request.Email, request.PreferredLanguage, request.IsActive);
        if (scope.IsPlatformAdmin && request.IsPlatformAdmin.HasValue) transaction.User.IsPlatformAdmin = request.IsPlatformAdmin.Value;
        if (transaction.User.LoginAccount is { } login)
        {
            login.Email = transaction.User.Email;
            if (request.IsLocalLoginEnabled.HasValue) login.IsEnabled = request.IsLocalLoginEnabled.Value;
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task AddUserRoleAsync(Guid userId, AddUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        RequireAccount(scope, request.AccountId);
        UserAdministrationRules.ValidateRoleScope(request);
        await using var transaction = await data.BeginAccessAsync(scope, userId, request.AccountId, cancellationToken);
        await Revalidate(scope, cancellationToken);
        if (request.BuildingId is Guid building && !await transaction.BuildingExistsAsync(building, cancellationToken))
            throw new InvalidOperationException("BuildingNotFound");
        if (request.OrganizationId is Guid organization && !await transaction.OrganizationExistsAsync(organization, cancellationToken))
            throw new InvalidOperationException("OrganizationNotFound");
        if (transaction.Roles.Any(x => x.Role == request.Role && x.OrganizationId == request.OrganizationId && x.BuildingId == request.BuildingId))
            throw new InvalidOperationException("UserRoleAlreadyExists");
        transaction.AddRole(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = transaction.Membership!.Id,
            Role = request.Role, OrganizationId = request.OrganizationId, BuildingId = request.BuildingId });
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task RemoveUserRoleAsync(Guid userId, Guid userAccountRoleId, CancellationToken cancellationToken = default)
    {
        var scope = await authorization.GetUserAdministrationScopeAsync(cancellationToken);
        var account = await data.ReadRoleAccountAsync(scope, userId, userAccountRoleId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        RequireAccount(scope, account);
        await using var transaction = await data.BeginAccessAsync(scope, userId, account, cancellationToken);
        await Revalidate(scope, cancellationToken);
        var role = transaction.Roles.SingleOrDefault(x => x.Id == userAccountRoleId) ?? throw new UnauthorizedAccessException();
        transaction.RemoveRole(role, transaction.Roles.Count == 1);
        await transaction.CommitAsync(cancellationToken);
    }
    private static void Apply(User user, string first, string last, string email, string language, bool active)
    { user.FirstName = first.Trim(); user.LastName = last.Trim(); user.Email = email.Trim(); user.PreferredLanguage = language; user.IsActive = active; }
}
