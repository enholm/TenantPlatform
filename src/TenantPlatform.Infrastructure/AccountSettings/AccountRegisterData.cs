using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.AccountSettings;

public sealed class AccountRegisterData(IDbContextFactory<TenantPlatformDbContext> factory) : IAccountRegisterData
{
    public async Task<IReadOnlyList<AccountRegisterEntry>> ReadAsync(Guid accountId, AccountRegisterKind kind, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadAsync(db, accountId, kind, ct);
    }

    private static async Task<List<AccountRegisterEntry>> ReadAsync(TenantPlatformDbContext db, Guid account, AccountRegisterKind kind, CancellationToken ct) => kind switch
    {
        AccountRegisterKind.OrganizationElements => await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == account)
            .OrderBy(x => x.Name).Select(x => new AccountRegisterEntry(x.Id, x.ParentId, x.Name, x.Description, x.IsActive)).ToListAsync(ct),
        AccountRegisterKind.GeographicAreas => await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == account)
            .OrderBy(x => x.Name).Select(x => new AccountRegisterEntry(x.Id, x.ParentId, x.Name, x.Description, x.IsActive)).ToListAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async Task<IAccountRegisterTransaction> BeginAsync(Guid accountId, AccountRegisterKind kind, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
            return new RegisterTransaction(db, transaction, accountId, kind, await ReadAsync(db, accountId, kind, ct));
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync();
            throw;
        }
    }

    private sealed class RegisterTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction,
        Guid account, AccountRegisterKind kind, IReadOnlyList<AccountRegisterEntry> entries) : IAccountRegisterTransaction
    {
        public IReadOnlyList<AccountRegisterEntry> Entries => entries;

        public Task<bool> IsReferencedAsync(Guid id, CancellationToken ct) => kind == AccountRegisterKind.OrganizationElements
            ? db.Agreements.AnyAsync(x => x.AccountId == account && x.OrganizationElementId == id, ct)
            : db.Agreements.AnyAsync(x => x.AccountId == account && x.GeographicAreaId == id, ct);

        public async Task SaveAsync(AccountRegisterEntry entry, CancellationToken ct)
        {
            if (kind == AccountRegisterKind.OrganizationElements)
            {
                var entity = await db.OrganizationElements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == entry.Id, ct);
                if (entity is null) { entity = new OrganizationElement { Id = entry.Id, AccountId = account }; db.OrganizationElements.Add(entity); }
                entity.ParentId = entry.ParentId; entity.Name = entry.Name; entity.Description = entry.Description; entity.IsActive = entry.IsActive;
            }
            else
            {
                var entity = await db.GeographicAreas.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == entry.Id, ct);
                if (entity is null) { entity = new GeographicArea { Id = entry.Id, AccountId = account }; db.GeographicAreas.Add(entity); }
                entity.ParentId = entry.ParentId; entity.Name = entry.Name; entity.Description = entry.Description; entity.IsActive = entry.IsActive;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct)
        {
            if (kind == AccountRegisterKind.OrganizationElements)
                db.OrganizationElements.Remove(await db.OrganizationElements.SingleAsync(x => x.AccountId == account && x.Id == id, ct));
            else db.GeographicAreas.Remove(await db.GeographicAreas.SingleAsync(x => x.AccountId == account && x.Id == id, ct));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" })
            { throw new AccountHierarchyException("AccountHierarchyUsedByAgreement"); }
            await transaction.CommitAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
