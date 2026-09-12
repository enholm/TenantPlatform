using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Localization;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Accounts;

public class AccountService : IAccountService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public AccountService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<AccountListItemDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.Accounts
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x =>
                new AccountListItemDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    DefaultLanguage = x.DefaultLanguage,
                    IsActive = x.IsActive
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountDetailsDto?> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x =>
                new AccountDetailsDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    DefaultLanguage = x.DefaultLanguage,
                    IsActive = x.IsActive
                })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(
            request.Name,
            request.DefaultLanguage);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var account =
            new Account
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                DefaultLanguage =
                    request.DefaultLanguage.Trim(),
                IsActive = request.IsActive
            };

        dbContext.Accounts.Add(account);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return account.Id;
    }

    public async Task UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(
            request.Name,
            request.DefaultLanguage);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var account =
            await dbContext.Accounts
                .SingleOrDefaultAsync(
                    x => x.Id == accountId,
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "AccountNotFound");
        }

        account.Name =
            request.Name.Trim();

        account.DefaultLanguage =
            request.DefaultLanguage.Trim();

        account.IsActive =
            request.IsActive;

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<AccountDeleteCheckResult>
        CanDeleteAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var exists =
            await dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == accountId,
                    cancellationToken);

        if (!exists)
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountNotFound");
        }

        //
        // An Account may only be physically deleted when it
        // contains no account-owned data.
        //

        if (await dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.Buildings
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.Units
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.Occupancies
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.NetworkEnvironments
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.NetworkSsids
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.ServiceDefinitions
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.ServiceDefinitionProviders
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.ServiceRequests
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.EmailOutboxMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed(
                "AccountHasData");
        }

        if (await dbContext.CalendarIntegrations.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId, cancellationToken))
        {
            return AccountDeleteCheckResult.NotAllowed("AccountHasData");
        }

        return AccountDeleteCheckResult.Allowed();
    }

    public async Task DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var deleteCheck =
            await CanDeleteAccountAsync(
                accountId,
                cancellationToken);

        if (!deleteCheck.CanDelete)
        {
            throw new AccountDeleteNotAllowedException(
                deleteCheck.Reason ??
                "AccountCannotBeDeleted");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var account =
            await dbContext.Accounts
                .SingleOrDefaultAsync(
                    x => x.Id == accountId,
                    cancellationToken);

        if (account is null)
        {
            throw new AccountDeleteNotAllowedException(
                "AccountNotFound");
        }

        var auditLogs =
            await dbContext.AuditLogs
                .Where(x =>
                    x.AccountId == accountId)
                .ToListAsync(cancellationToken);

        dbContext.AuditLogs.RemoveRange(auditLogs);

        dbContext.Accounts.Remove(account);

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            //
            // Defensive check in case a new Account-owned entity
            // is introduced later but is not yet covered by
            // CanDeleteAccountAsync().
            //
            throw new AccountDeleteNotAllowedException(
                "AccountHasData");
        }
    }

    private static void ValidateRequest(
        string name,
        string defaultLanguage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "AccountNameRequired");
        }

        if (name.Trim().Length > 200)
        {
            throw new InvalidOperationException(
                "AccountNameTooLong");
        }

        if (string.IsNullOrWhiteSpace(defaultLanguage))
        {
            throw new InvalidOperationException(
                "AccountDefaultLanguageRequired");
        }

        var languageSupported =
            SupportedLanguages.All.Any(
                x => string.Equals(
                    x.Code,
                    defaultLanguage.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (!languageSupported)
        {
            throw new InvalidOperationException(
                "AccountDefaultLanguageNotSupported");
        }
    }
}
