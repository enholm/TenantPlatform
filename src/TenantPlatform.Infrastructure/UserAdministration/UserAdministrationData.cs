using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.UserAdministration;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.UserAdministration;

public sealed class UserAdministrationData(IDbContextFactory<TenantPlatformDbContext> factory) : IUserAdministrationData
{
    private static IQueryable<User> Visible(TenantPlatformDbContext db, UserAdministrationScope scope) =>
        db.Users.Where(x => scope.IsPlatformAdmin || x.Accounts.Any(m => m.AccountId == scope.AccountId));
    public async Task<List<UserListItemDto>> ReadUsersAsync(UserAdministrationScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Visible(db, scope).AsNoTracking().OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
            .Select(x => new UserListItemDto { Id = x.Id, FirstName = x.FirstName, LastName = x.LastName, Email = x.Email,
                PreferredLanguage = x.PreferredLanguage, IsActive = x.IsActive, IsPlatformAdmin = x.IsPlatformAdmin,
                HasLocalLogin = x.LoginAccount != null, IsLocalLoginEnabled = x.LoginAccount != null && x.LoginAccount.IsEnabled }).ToListAsync(ct);
    }
    public async Task<UserDetailsDto?> ReadUserAsync(UserAdministrationScope scope, Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await Visible(db, scope).AsNoTracking().Include(x => x.LoginAccount).SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return null;
        return new UserDetailsDto { Id = user.Id, FirstName = user.FirstName, LastName = user.LastName, Email = user.Email,
            PreferredLanguage = user.PreferredLanguage, IsActive = user.IsActive, IsPlatformAdmin = user.IsPlatformAdmin,
            LocalLogin = user.LoginAccount is not { } login ? null : new LocalLoginDto { Email = login.Email, IsEnabled = login.IsEnabled,
                FailedLoginCount = login.FailedLoginCount, LockedUntilUtc = login.LockedUntilUtc, LastLoginUtc = login.LastLoginUtc, CreatedUtc = login.CreatedUtc },
            Accounts = (await GetUserAccessInternalAsync(db, userId, scope.AccountId, scope.IsPlatformAdmin, ct)).ToList() };
    }
    public async Task<IReadOnlyList<UserAccountAccessDto>> ReadAccessAsync(UserAdministrationScope scope, Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await GetUserAccessInternalAsync(db, userId, scope.AccountId, scope.IsPlatformAdmin, ct);
    }
    public async Task<IReadOnlyList<UserAccountOptionDto>> ReadAccountsAsync(UserAdministrationScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().Where(x => x.IsActive && (scope.IsPlatformAdmin || x.Id == scope.AccountId))
            .OrderBy(x => x.Name).Select(x => new UserAccountOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<UserOrganizationOptionDto>> ReadOrganizationsAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().Where(x => x.AccountId == accountId && x.IsActive).OrderBy(x => x.Name)
            .Select(x => new UserOrganizationOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<UserBuildingOptionDto>> ReadBuildingsAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Buildings.AsNoTracking().Where(x => x.AccountId == accountId && x.IsActive).OrderBy(x => x.Name)
            .Select(x => new UserBuildingOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
    }
    public async Task<Guid?> ReadRoleAccountAsync(UserAdministrationScope scope, Guid userId, Guid roleId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.UserAccountRoles.AsNoTracking().Where(x => x.Id == roleId && x.UserAccount.UserId == userId &&
            (scope.IsPlatformAdmin || x.UserAccount.AccountId == scope.AccountId)).Select(x => (Guid?)x.UserAccount.AccountId).SingleOrDefaultAsync(ct);
    }
    public Task<IUserAdministrationTransaction> BeginUserAsync(UserAdministrationScope scope, Guid? userId, Guid? createAccountId, CancellationToken ct) =>
        BeginAsync(scope, userId, createAccountId, false, ct);
    public Task<IUserAdministrationTransaction> BeginAccessAsync(UserAdministrationScope scope, Guid userId, Guid accountId, CancellationToken ct) =>
        BeginAsync(scope, userId, accountId, true, ct);
    private async Task<IUserAdministrationTransaction> BeginAsync(UserAdministrationScope scope, Guid? userId, Guid? account, bool access, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            if (account is Guid accountId)
            {
                if (!scope.IsPlatformAdmin && scope.AccountId != account) throw new UnauthorizedAccessException();
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
                if (!await db.Accounts.AnyAsync(x => x.Id == accountId, ct)) throw new InvalidOperationException("AccountNotFound");
            }
            User user;
            if (userId.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE \"Id\" = {userId.Value} FOR UPDATE", ct);
                var visible = access ? db.Users.AsQueryable() : Visible(db, scope);
                user = await visible.Include(x => x.LoginAccount).SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new UnauthorizedAccessException();
            }
            else { user = new User { Id = Guid.NewGuid() }; db.Users.Add(user); }
            UserAccount? member = null; List<UserAccountRole> roles = [];
            if (account.HasValue)
            {
                member = await db.UserAccounts.SingleOrDefaultAsync(x => x.AccountId == account && x.UserId == user.Id, ct);
                if (member is null) { member = new UserAccount { Id = Guid.NewGuid(), AccountId = account.Value, UserId = user.Id }; db.UserAccounts.Add(member); }
                else roles = await db.UserAccountRoles.Where(x => x.UserAccountId == member.Id).ToListAsync(ct);
            }
            return new WriteTransaction(db, tx, user, member, roles, userId.HasValue && !access && !scope.IsPlatformAdmin ? scope.AccountId : null);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class WriteTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, User user, UserAccount? member,
        List<UserAccountRole> roles, Guid? requiredMembership) : IUserAdministrationTransaction
    {
        public User User => user;
        public UserAccount? Membership => member;
        public IReadOnlyList<UserAccountRole> Roles => roles;
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct) => db.Users.AnyAsync(x => x.Id != user.Id && x.Email.ToLower() == email, ct);
        public Task<bool> BuildingExistsAsync(Guid id, CancellationToken ct) => db.Buildings.AnyAsync(x => x.AccountId == member!.AccountId && x.Id == id, ct);
        public Task<bool> OrganizationExistsAsync(Guid id, CancellationToken ct) => db.Organizations.AnyAsync(x => x.AccountId == member!.AccountId && x.Id == id, ct);
        public void AddLogin(LoginAccount login) { if (login.UserId != user.Id) throw new UnauthorizedAccessException(); db.LoginAccounts.Add(login); }
        public void AddRole(UserAccountRole role) { if (role.UserAccountId != member!.Id) throw new UnauthorizedAccessException(); db.UserAccountRoles.Add(role); }
        public void RemoveRole(UserAccountRole role, bool removeMembership)
        { if (!roles.Contains(role)) throw new UnauthorizedAccessException(); db.UserAccountRoles.Remove(role); if (removeMembership) db.UserAccounts.Remove(member!); }
        public async Task CommitAsync(CancellationToken ct)
        {
            if (requiredMembership.HasValue && !await db.UserAccounts.AnyAsync(x => x.AccountId == requiredMembership && x.UserId == user.Id, ct)) throw new UnauthorizedAccessException();
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
    private static async Task<IReadOnlyList<UserAccountAccessDto>>
        GetUserAccessInternalAsync(
            TenantPlatformDbContext dbContext,
            Guid userId,
            Guid? accountId,
            bool includeAllAccounts,
            CancellationToken cancellationToken)
    {
        var query =
            dbContext.UserAccounts
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId);

        if (!includeAllAccounts)
        {
            if (!accountId.HasValue)
            {
                return [];
            }

            query =
                query.Where(x =>
                    x.AccountId ==
                    accountId.Value);
        }

        var accounts =
            await query
                .Select(x =>
                    new
                    {
                        x.Id,
                        x.AccountId,

                        AccountName =
                            x.Account.Name
                    })
                .OrderBy(x =>
                    x.AccountName)
                .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return [];
        }

        var userAccountIds =
            accounts
                .Select(x => x.Id)
                .ToList();

        var roles =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .Where(x =>
                    userAccountIds.Contains(
                        x.UserAccountId))
                .Select(x =>
                    new
                    {
                        x.Id,
                        x.UserAccountId,
                        x.Role,
                        x.OrganizationId,

                        OrganizationName =
                            x.OrganizationId.HasValue
                                ? dbContext.Organizations
                                    .Where(o =>
                                        o.AccountId == x.UserAccount.AccountId && o.Id ==
                                        x.OrganizationId.Value)
                                    .Select(o => o.Name)
                                    .FirstOrDefault()
                                : null,

                        x.BuildingId,

                        BuildingName =
                            x.BuildingId.HasValue
                                ? dbContext.Buildings
                                    .Where(b =>
                                        b.AccountId == x.UserAccount.AccountId && b.Id ==
                                        x.BuildingId.Value)
                                    .Select(b => b.Name)
                                    .FirstOrDefault()
                                : null
                    })
                .ToListAsync(cancellationToken);

        return accounts
            .Select(account =>
                new UserAccountAccessDto
                {
                    UserAccountId =
                        account.Id,

                    AccountId =
                        account.AccountId,

                    AccountName =
                        account.AccountName,

                    Roles =
                        roles
                            .Where(x =>
                                x.UserAccountId ==
                                account.Id)
                            .Select(x =>
                                new UserRoleAssignmentDto
                                {
                                    Id =
                                        x.Id,

                                    Role =
                                        x.Role,

                                    OrganizationId =
                                        x.OrganizationId,

                                    OrganizationName =
                                        x.OrganizationName,

                                    BuildingId =
                                        x.BuildingId,

                                    BuildingName =
                                        x.BuildingName
                                })
                            .OrderBy(x => x.Role)
                            .ToList()
                })
            .ToList();
    }

}
