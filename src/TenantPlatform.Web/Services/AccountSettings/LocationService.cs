using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class LocationService(
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

    public async Task<List<Location>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var accountId = await RequireAccountAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Locations.AsNoTracking().Where(x => x.AccountId == accountId)
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
        Location entity;
        if (id.HasValue)
        {
            entity = await db.Locations.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.Id == id.Value, cancellationToken)
                ?? throw new InvalidOperationException("AccountRegisterNotFound");
        }
        else
        {
            entity = new Location { Id = Guid.NewGuid(), AccountId = accountId };
            db.Locations.Add(entity);
        }
        entity.Name = name;
        entity.Description = description;
        entity.IsActive = input.IsActive;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accountId = await RequireAccountAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Locations.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("AccountRegisterNotFound");
        db.Locations.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
