using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Organizations;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Organizations;

public sealed class OrganizationData(IDbContextFactory<TenantPlatformDbContext> factory) : IOrganizationData
{
    public async Task<List<OrganizationListItemDto>> ReadListAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.Organizations
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                OrganizationNumber = x.OrganizationNumber,
                Type = x.Type,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationDetailsDto?> ReadDetailsAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.Organizations
            .AsNoTracking()
            .Where(x =>
                x.Id == organizationId &&
                x.AccountId == accountId)
            .Select(x => new OrganizationDetailsDto
            {
                Id = x.Id,
                Name = x.Name,
                OrganizationNumber = x.OrganizationNumber,
                Type = x.Type,
                IsActive = x.IsActive,

                OccupancyCount = dbContext.Occupancies
                    .Count(o =>
                        o.AccountId == accountId && o.TenantOrganizationId == x.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<IOrganizationTransaction> BeginAsync(Guid accountId, Guid? id, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            Organization entity;
            if (id.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM organizations WHERE \"Id\" = {id.Value} AND \"AccountId\" = {accountId} FOR UPDATE", ct);
                entity = await db.Organizations.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == id, ct) ?? throw new UnauthorizedAccessException();
            }
            else { entity = new Organization { Id = Guid.NewGuid(), AccountId = accountId }; db.Organizations.Add(entity); }
            return new OrganizationTransaction(db, transaction, entity);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync(); throw;
        }
    }
    private sealed class OrganizationTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction, Organization entity) : IOrganizationTransaction
    {
        public Organization Organization => entity;
        public async Task<OrganizationReferences> ReadReferencesAsync(CancellationToken ct) => new(
            await db.Occupancies.AnyAsync(x => x.AccountId == entity.AccountId && x.TenantOrganizationId == entity.Id, ct),
            await db.NetworkSsids.AnyAsync(x => x.AccountId == entity.AccountId && x.TenantOrganizationId == entity.Id, ct),
            await db.ServiceRequests.AnyAsync(x => x.AccountId == entity.AccountId && x.RequesterOrganizationId == entity.Id, ct),
            await db.UserAccountRoles.AnyAsync(x => x.UserAccount.AccountId == entity.AccountId && x.OrganizationId == entity.Id, ct),
            await db.Agreements.AnyAsync(x => x.AccountId == entity.AccountId && x.CounterpartyOrganizationId == entity.Id, ct));
        public async Task CommitAsync(bool delete, CancellationToken ct)
        {
            if (delete) db.Organizations.Remove(entity);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
