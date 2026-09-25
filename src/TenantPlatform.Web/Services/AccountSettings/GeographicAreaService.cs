using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class GeographicAreaService(
    IDbContextFactory<TenantPlatformDbContext> dbFactory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization)
{
    private async Task<Guid> RequireAccountAsync(CancellationToken cancellationToken)
    {
        var accountId = userContext.Current.CurrentAccountId;
        if (!userContext.Current.IsAuthenticated || !accountId.HasValue ||
            !await authorization.CanManageAccountStructureAsync(cancellationToken))
            throw new UnauthorizedAccessException();
        return accountId.Value;
    }

    public async Task<List<GeographicArea>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var accountId = await RequireAccountAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Guid? id, AccountRegisterInput input, CancellationToken cancellationToken = default)
    {
        var accountId = await RequireAccountAsync(cancellationToken);
        var name = input.Name?.Trim();
        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || description?.Length > 2000)
            throw new ArgumentException("AccountRegisterInvalidInput");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAccountAsync(db, accountId, cancellationToken);
        await ValidateParentAsync(db, accountId, id, input.ParentId, cancellationToken);
        GeographicArea entity;
        if (id.HasValue)
        {
            entity = await db.GeographicAreas.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.Id == id.Value, cancellationToken)
                ?? throw new InvalidOperationException("AccountRegisterNotFound");
        }
        else
        {
            entity = new GeographicArea { Id = Guid.NewGuid(), AccountId = accountId };
            db.GeographicAreas.Add(entity);
        }
        entity.ParentId = input.ParentId;
        entity.Name = name;
        entity.Description = description;
        entity.IsActive = input.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accountId = await RequireAccountAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAccountAsync(db, accountId, cancellationToken);
        var entity = await db.GeographicAreas.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("AccountRegisterNotFound");
        if (await db.GeographicAreas.AnyAsync(x => x.AccountId == accountId && x.ParentId == id, cancellationToken))
            throw new AccountHierarchyException("AccountHierarchyHasChildren");
        db.GeographicAreas.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task LockAccountAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken cancellationToken)
    {
        // All hierarchy mutations for this account take the same row lock before reading parents.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", cancellationToken);
    }

    private static async Task ValidateParentAsync(TenantPlatformDbContext db, Guid accountId,
        Guid? id, Guid? parentId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue) return;
        var parents = await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == accountId)
            .ToDictionaryAsync(x => x.Id, x => x.ParentId, cancellationToken);
        var visited = new HashSet<Guid>();
        while (parentId.HasValue)
        {
            if (parentId == id || !visited.Add(parentId.Value))
                throw new AccountHierarchyException("AccountHierarchyCycle");
            if (!parents.TryGetValue(parentId.Value, out var next))
                throw new AccountHierarchyException("AccountHierarchyInvalidParent");
            parentId = next;
        }
    }
}
