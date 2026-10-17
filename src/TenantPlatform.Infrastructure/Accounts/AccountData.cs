using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Accounts;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Accounts;

public sealed class AccountData(IDbContextFactory<TenantPlatformDbContext> factory) : IAccountData
{
    public async Task<List<AccountListItemDto>> ReadListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
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

    public async Task<AccountDetailsDto?> ReadAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
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


    public async Task<IAccountTransaction> BeginAsync(Guid? id, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            Account account;
            if (id.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {id.Value} FOR UPDATE", ct);
                account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
            }
            else { account = new Account { Id = Guid.NewGuid() }; db.Accounts.Add(account); }
            return new AccountTransaction(db, transaction, account);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync(); throw;
        }
    }
    private sealed class AccountTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction, Account account) : IAccountTransaction
    {
        public Account Account => account;
        public async Task<bool> HasOwnedDataAsync(CancellationToken ct)
        {
            var accountId = account.Id;
        //
        // An Account may only be physically deleted when it
        // contains no account-owned data.
        //

        if (await db.UserAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.OrganizationElements.AsNoTracking().AnyAsync(x => x.AccountId == accountId, ct) ||
            await db.GeographicAreas.AsNoTracking().AnyAsync(x => x.AccountId == accountId, ct))
        {
            return true;
        }

        if (await db.Organizations
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.Buildings
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.Units
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.Occupancies
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.NetworkEnvironments
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.NetworkSsids
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.ServiceDefinitions
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.ServiceDefinitionProviders
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.ServiceRequests
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.EmailOutboxMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.AccountId == accountId,
                ct))
        {
            return true;
        }

        if (await db.CalendarIntegrations.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId, ct))
        {
            return true;
        }


            return false;
        }
        public async Task CommitAsync(bool delete, CancellationToken ct)
        {
            if (delete)
            {
                var logs = await db.AuditLogs.Where(x => x.AccountId == account.Id).ToListAsync(ct);
                db.AuditLogs.RemoveRange(logs);
                db.Accounts.Remove(account);
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) when (delete) { throw new AccountDeleteNotAllowedException("AccountHasData"); }
            await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
