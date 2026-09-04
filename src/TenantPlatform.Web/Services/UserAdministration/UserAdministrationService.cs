using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Localization;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.UserAdministration;

public class UserAdministrationService
    : IUserAdministrationService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    private readonly PasswordService
        _passwordService;

    private readonly ICurrentUserContextService
        _currentUserContextService;

    public UserAdministrationService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
        PasswordService passwordService,
        ICurrentUserContextService currentUserContextService)
    {
        _dbContextFactory =
            dbContextFactory;

        _passwordService =
            passwordService;

        _currentUserContextService =
            currentUserContextService;
    }

    public async Task<List<UserListItemDto>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        var query =
            dbContext.Users
                .AsNoTracking()
                .AsQueryable();

        if (!currentUser.IsPlatformAdmin)
        {
            if (!currentUser.CurrentAccountId.HasValue)
            {
                return [];
            }

            var accountId =
                currentUser.CurrentAccountId.Value;

            var isAccountAdmin =
                await IsAccountAdminAsync(
                    dbContext,
                    currentUser.UserId,
                    accountId,
                    cancellationToken);

            if (!isAccountAdmin)
            {
                return [];
            }

            query =
                query.Where(user =>
                    user.Accounts.Any(x =>
                        x.AccountId == accountId));
        }

        return await query
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Select(x =>
                new UserListItemDto
                {
                    Id =
                        x.Id,

                    FirstName =
                        x.FirstName,

                    LastName =
                        x.LastName,

                    Email =
                        x.Email,

                    PreferredLanguage =
                        x.PreferredLanguage,

                    IsActive =
                        x.IsActive,

                    IsPlatformAdmin =
                        x.IsPlatformAdmin,

                    HasLocalLogin =
                        x.LoginAccount != null,

                    IsLocalLoginEnabled =
                        x.LoginAccount != null &&
                        x.LoginAccount.IsEnabled
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDetailsDto?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            return null;
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        if (!await CanViewUserAsync(
            dbContext,
            currentUser.UserId,
            currentUser.IsPlatformAdmin,
            currentUser.CurrentAccountId,
            userId,
            cancellationToken))
        {
            return null;
        }

        var user =
            await dbContext.Users
                .AsNoTracking()
                .Include(x => x.LoginAccount)
                .SingleOrDefaultAsync(
                    x => x.Id == userId,
                    cancellationToken);

        if (user is null)
        {
            return null;
        }

        var accounts =
            await GetUserAccessInternalAsync(
                dbContext,
                userId,
                currentUser.IsPlatformAdmin
                    ? null
                    : currentUser.CurrentAccountId,
                currentUser.IsPlatformAdmin,
                cancellationToken);

        return new UserDetailsDto
        {
            Id =
                user.Id,

            FirstName =
                user.FirstName,

            LastName =
                user.LastName,

            Email =
                user.Email,

            PreferredLanguage =
                user.PreferredLanguage,

            IsActive =
                user.IsActive,

            IsPlatformAdmin =
                user.IsPlatformAdmin,

            LocalLogin =
                user.LoginAccount is null
                    ? null
                    : new LocalLoginDto
                    {
                        Email =
                            user.LoginAccount.Email,

                        IsEnabled =
                            user.LoginAccount.IsEnabled,

                        FailedLoginCount =
                            user.LoginAccount.FailedLoginCount,

                        LockedUntilUtc =
                            user.LoginAccount.LockedUntilUtc,

                        LastLoginUtc =
                            user.LoginAccount.LastLoginUtc,

                        CreatedUtc =
                            user.LoginAccount.CreatedUtc
                    },

            Accounts =
                accounts.ToList()
        };
    }

    public async Task<Guid> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "UserAdministrationNotAllowed");
        }

        Guid? effectiveAccountId =
            request.AccountId;

        if (!currentUser.IsPlatformAdmin)
        {
            if (!currentUser.CurrentAccountId.HasValue)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }

            await using var authorizationDbContext =
                await _dbContextFactory
                    .CreateDbContextAsync(cancellationToken);

            var isAccountAdmin =
                await IsAccountAdminAsync(
                    authorizationDbContext,
                    currentUser.UserId,
                    currentUser.CurrentAccountId.Value,
                    cancellationToken);

            if (!isAccountAdmin)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }

            effectiveAccountId =
                currentUser.CurrentAccountId.Value;
        }

        ValidateUserRequest(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PreferredLanguage);

        if (request.CreateLocalLogin &&
            string.IsNullOrWhiteSpace(
                request.TemporaryPassword))
        {
            throw new InvalidOperationException(
                "TemporaryPasswordRequired");
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        var normalizedEmail =
            NormalizeEmail(request.Email);

        var emailExists =
            await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Email.ToLower() ==
                        normalizedEmail,
                    cancellationToken);

        if (emailExists)
        {
            throw new InvalidOperationException(
                "UserEmailAlreadyExists");
        }

        var user =
            new User
            {
                Id =
                    Guid.NewGuid(),

                FirstName =
                    request.FirstName.Trim(),

                LastName =
                    request.LastName.Trim(),

                Email =
                    request.Email.Trim(),

                PreferredLanguage =
                    request.PreferredLanguage,

                IsActive =
                    request.IsActive,

                IsPlatformAdmin =
                    false
            };

        dbContext.Users.Add(user);

        if (request.CreateLocalLogin)
        {
            var loginAccount =
                new LoginAccount
                {
                    Id =
                        Guid.NewGuid(),

                    UserId =
                        user.Id,

                    Email =
                        user.Email,

                    IsEnabled =
                        request.IsLocalLoginEnabled,

                    FailedLoginCount =
                        0,

                    CreatedUtc =
                        DateTimeOffset.UtcNow
                };

            loginAccount.PasswordHash =
                _passwordService.HashPassword(
                    loginAccount,
                    request.TemporaryPassword!);

            dbContext.LoginAccounts.Add(
                loginAccount);
        }

        if (effectiveAccountId.HasValue)
        {
            var accountExists =
                await dbContext.Accounts
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.Id ==
                            effectiveAccountId.Value,
                        cancellationToken);

            if (!accountExists)
            {
                throw new InvalidOperationException(
                    "AccountNotFound");
            }

            dbContext.UserAccounts.Add(
                new UserAccount
                {
                    Id =
                        Guid.NewGuid(),

                    UserId =
                        user.Id,

                    AccountId =
                        effectiveAccountId.Value
                });
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return user.Id;
    }

    public async Task UpdateUserAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "UserAdministrationNotAllowed");
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        if (!await CanViewUserAsync(
            dbContext,
            currentUser.UserId,
            currentUser.IsPlatformAdmin,
            currentUser.CurrentAccountId,
            userId,
            cancellationToken))
        {
            throw new InvalidOperationException(
                "UserAdministrationNotAllowed");
        }

        ValidateUserRequest(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PreferredLanguage);

        var user =
            await dbContext.Users
                .Include(x => x.LoginAccount)
                .SingleOrDefaultAsync(
                    x => x.Id == userId,
                    cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException(
                "UserNotFound");
        }

        var normalizedEmail =
            NormalizeEmail(request.Email);

        var emailExists =
            await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id != userId &&
                        x.Email.ToLower() ==
                        normalizedEmail,
                    cancellationToken);

        if (emailExists)
        {
            throw new InvalidOperationException(
                "UserEmailAlreadyExists");
        }

        user.FirstName =
            request.FirstName.Trim();

        user.LastName =
            request.LastName.Trim();

        user.Email =
            request.Email.Trim();

        user.PreferredLanguage =
            request.PreferredLanguage;

        user.IsActive =
            request.IsActive;

        if (currentUser.IsPlatformAdmin &&
            request.IsPlatformAdmin.HasValue)
        {
            user.IsPlatformAdmin =
                request.IsPlatformAdmin.Value;
        }

        if (user.LoginAccount is not null)
        {
            user.LoginAccount.Email =
                user.Email;

            if (request.IsLocalLoginEnabled.HasValue)
            {
                user.LoginAccount.IsEnabled =
                    request.IsLocalLoginEnabled.Value;
            }
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<UserAccountAccessDto>>
        GetUserAccessAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        if (!await CanViewUserAsync(
            dbContext,
            currentUser.UserId,
            currentUser.IsPlatformAdmin,
            currentUser.CurrentAccountId,
            userId,
            cancellationToken))
        {
            return [];
        }

        return await GetUserAccessInternalAsync(
            dbContext,
            userId,
            currentUser.IsPlatformAdmin
                ? null
                : currentUser.CurrentAccountId,
            currentUser.IsPlatformAdmin,
            cancellationToken);
    }

    public async Task AddUserRoleAsync(
        Guid userId,
        AddUserRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "UserAdministrationNotAllowed");
        }

        if (!currentUser.IsPlatformAdmin)
        {
            if (!currentUser.CurrentAccountId.HasValue ||
                currentUser.CurrentAccountId.Value !=
                    request.AccountId)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }

            await using var authorizationDbContext =
                await _dbContextFactory
                    .CreateDbContextAsync(cancellationToken);

            var isAccountAdmin =
                await IsAccountAdminAsync(
                    authorizationDbContext,
                    currentUser.UserId,
                    request.AccountId,
                    cancellationToken);

            if (!isAccountAdmin)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }
        }

        ValidateRoleScope(request);

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        var userExists =
            await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == userId,
                    cancellationToken);

        if (!userExists)
        {
            throw new InvalidOperationException(
                "UserNotFound");
        }

        var accountExists =
            await dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == request.AccountId,
                    cancellationToken);

        if (!accountExists)
        {
            throw new InvalidOperationException(
                "AccountNotFound");
        }

        await ValidateRoleScopeEntitiesAsync(
            dbContext,
            request,
            cancellationToken);

        var userAccount =
            await dbContext.UserAccounts
                .SingleOrDefaultAsync(
                    x =>
                        x.UserId == userId &&
                        x.AccountId ==
                            request.AccountId,
                    cancellationToken);

        if (userAccount is null)
        {
            userAccount =
                new UserAccount
                {
                    Id =
                        Guid.NewGuid(),

                    UserId =
                        userId,

                    AccountId =
                        request.AccountId
                };

            dbContext.UserAccounts.Add(
                userAccount);
        }

        var roleExists =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccount.UserId ==
                            userId &&
                        x.UserAccount.AccountId ==
                            request.AccountId &&
                        x.Role ==
                            request.Role &&
                        x.OrganizationId ==
                            request.OrganizationId &&
                        x.BuildingId ==
                            request.BuildingId,
                    cancellationToken);

        if (roleExists)
        {
            throw new InvalidOperationException(
                "UserRoleAlreadyExists");
        }

        dbContext.UserAccountRoles.Add(
            new UserAccountRole
            {
                Id =
                    Guid.NewGuid(),

                UserAccountId =
                    userAccount.Id,

                Role =
                    request.Role,

                OrganizationId =
                    request.OrganizationId,

                BuildingId =
                    request.BuildingId
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task RemoveUserRoleAsync(
        Guid userId,
        Guid userAccountRoleId,
        CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "UserAdministrationNotAllowed");
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        var role =
            await dbContext.UserAccountRoles
                .Include(x => x.UserAccount)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == userAccountRoleId &&
                        x.UserAccount.UserId ==
                            userId,
                    cancellationToken);

        if (role is null)
        {
            throw new InvalidOperationException(
                "UserRoleNotFound");
        }

        if (!currentUser.IsPlatformAdmin)
        {
            if (!currentUser.CurrentAccountId.HasValue ||
                currentUser.CurrentAccountId.Value !=
                    role.UserAccount.AccountId)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }

            var isAccountAdmin =
                await IsAccountAdminAsync(
                    dbContext,
                    currentUser.UserId,
                    role.UserAccount.AccountId,
                    cancellationToken);

            if (!isAccountAdmin)
            {
                throw new InvalidOperationException(
                    "UserAdministrationNotAllowed");
            }
        }

        var userAccount =
            role.UserAccount;

        dbContext.UserAccountRoles.Remove(
            role);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        var hasRemainingRoles =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccountId ==
                        userAccount.Id,
                    cancellationToken);

        if (!hasRemainingRoles)
        {
            dbContext.UserAccounts.Remove(
                userAccount);

            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UserAccountOptionDto>>
        GetAvailableAccountsAsync(
            CancellationToken cancellationToken = default)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        if (currentUser.IsPlatformAdmin)
        {
            return await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x =>
                    new UserAccountOptionDto
                    {
                        Id = x.Id,
                        Name = x.Name
                    })
                .ToListAsync(cancellationToken);
        }

        if (!currentUser.CurrentAccountId.HasValue)
        {
            return [];
        }

        var accountId =
            currentUser.CurrentAccountId.Value;

        var isAccountAdmin =
            await IsAccountAdminAsync(
                dbContext,
                currentUser.UserId,
                accountId,
                cancellationToken);

        if (!isAccountAdmin)
        {
            return [];
        }

        return await dbContext.Accounts
            .AsNoTracking()
            .Where(x =>
                x.Id == accountId &&
                x.IsActive)
            .Select(x =>
                new UserAccountOptionDto
                {
                    Id = x.Id,
                    Name = x.Name
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserOrganizationOptionDto>>
        GetAvailableOrganizationsAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        if (!await CanManageAccountAsync(
            accountId,
            cancellationToken))
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        return await dbContext.Organizations
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x =>
                new UserOrganizationOptionDto
                {
                    Id = x.Id,
                    Name = x.Name
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserBuildingOptionDto>>
        GetAvailableBuildingsAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        if (!await CanManageAccountAsync(
            accountId,
            cancellationToken))
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        return await dbContext.Buildings
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x =>
                new UserBuildingOptionDto
                {
                    Id = x.Id,
                    Name = x.Name
                })
            .ToListAsync(cancellationToken);
    }
    private static async Task<bool> CanViewUserAsync(
        TenantPlatformDbContext dbContext,
        Guid currentUserId,
        bool isPlatformAdmin,
        Guid? currentAccountId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (isPlatformAdmin)
        {
            return await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == targetUserId,
                    cancellationToken);
        }

        if (!currentAccountId.HasValue)
        {
            return false;
        }

        var isAccountAdmin =
            await IsAccountAdminAsync(
                dbContext,
                currentUserId,
                currentAccountId.Value,
                cancellationToken);

        if (!isAccountAdmin)
        {
            return false;
        }

        return await dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.UserId == targetUserId &&
                    x.AccountId ==
                        currentAccountId.Value,
                cancellationToken);
    }

    private static Task<bool> IsAccountAdminAsync(
        TenantPlatformDbContext dbContext,
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return dbContext.UserAccountRoles
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.UserAccount.UserId == userId &&
                    x.UserAccount.AccountId == accountId &&
                    x.Role == UserRole.AccountAdmin,
                cancellationToken);
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
                                        o.Id ==
                                        x.OrganizationId.Value)
                                    .Select(o => o.Name)
                                    .FirstOrDefault()
                                : null,

                        x.BuildingId,

                        BuildingName =
                            x.BuildingId.HasValue
                                ? dbContext.Buildings
                                    .Where(b =>
                                        b.Id ==
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

    private static void ValidateUserRequest(
        string firstName,
        string lastName,
        string email,
        string preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new InvalidOperationException(
                "FirstNameRequired");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException(
                "LastNameRequired");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "EmailRequired");
        }

        if (!SupportedLanguages.All.Any(x =>
            string.Equals(
                x.Code,
                preferredLanguage,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "UnsupportedLanguage");
        }
    }

    private static void ValidateRoleScope(
        AddUserRoleRequest request)
    {
        switch (request.Role)
        {
            case UserRole.AccountAdmin:

                if (request.OrganizationId.HasValue ||
                    request.BuildingId.HasValue)
                {
                    throw new InvalidOperationException(
                        "AccountAdminCannotHaveScope");
                }

                break;

            case UserRole.PropertyAdmin:

                if (!request.BuildingId.HasValue ||
                    request.OrganizationId.HasValue)
                {
                    throw new InvalidOperationException(
                        "PropertyAdminRequiresBuilding");
                }

                break;

            case UserRole.TenantAdmin:
            case UserRole.TenantUser:
            case UserRole.ServiceProviderUser:

                if (!request.OrganizationId.HasValue ||
                    request.BuildingId.HasValue)
                {
                    throw new InvalidOperationException(
                        "OrganizationRoleRequiresOrganization");
                }

                break;

            default:

                throw new InvalidOperationException(
                    "UnsupportedUserRole");
        }
    }

    private static async Task ValidateRoleScopeEntitiesAsync(
        TenantPlatformDbContext dbContext,
        AddUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BuildingId.HasValue)
        {
            var buildingExists =
                await dbContext.Buildings
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.Id ==
                                request.BuildingId.Value &&
                            x.AccountId ==
                                request.AccountId,
                        cancellationToken);

            if (!buildingExists)
            {
                throw new InvalidOperationException(
                    "BuildingNotFound");
            }
        }

        if (request.OrganizationId.HasValue)
        {
            var organizationExists =
                await dbContext.Organizations
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.Id ==
                                request.OrganizationId.Value &&
                            x.AccountId ==
                                request.AccountId,
                        cancellationToken);

            if (!organizationExists)
            {
                throw new InvalidOperationException(
                    "OrganizationNotFound");
            }
        }
    }

    private static string NormalizeEmail(
        string email)
    {
        return email
            .Trim()
            .ToLowerInvariant();
    }

    private async Task<bool> CanManageAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var currentUser =
            _currentUserContextService.Current;

        if (!currentUser.IsAuthenticated)
        {
            return false;
        }

        if (currentUser.IsPlatformAdmin)
        {
            return true;
        }

        if (!currentUser.CurrentAccountId.HasValue ||
            currentUser.CurrentAccountId.Value != accountId)
        {
            return false;
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

        return await IsAccountAdminAsync(
            dbContext,
            currentUser.UserId,
            accountId,
            cancellationToken);
    }
}

