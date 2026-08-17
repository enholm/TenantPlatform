using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Organizations;

public class OrganizationService : IOrganizationService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public OrganizationService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<OrganizationListItemDto>> GetOrganizationsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
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

    public async Task<OrganizationDetailsDto?> GetOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
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
                        o.TenantOrganizationId == x.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> CreateOrganizationAsync(
        Guid accountId,
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = request.Name.Trim(),
            OrganizationNumber =
                NormalizeOptional(request.OrganizationNumber),
            Type = request.Type,
            IsActive = request.IsActive
        };

        dbContext.Organizations.Add(organization);

        await dbContext.SaveChangesAsync(cancellationToken);

        return organization.Id;
    }

    public async Task UpdateOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var organization = await dbContext.Organizations
            .SingleOrDefaultAsync(
                x =>
                    x.Id == organizationId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (organization is null)
        {
            throw new InvalidOperationException(
                "Organization was not found in the current account.");
        }

        organization.Name = request.Name.Trim();

        organization.OrganizationNumber =
            NormalizeOptional(request.OrganizationNumber);

        organization.Type = request.Type;
        organization.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrganizationDeleteCheckResult>
        CanDeleteOrganizationAsync(
            Guid accountId,
            Guid organizationId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var organizationExists =
            await dbContext.Organizations
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == organizationId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!organizationExists)
        {
            return OrganizationDeleteCheckResult.NotAllowed(
                "OrganizationNotFound");
        }

        var hasOccupancies =
            await dbContext.Occupancies
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.TenantOrganizationId == organizationId,
                    cancellationToken);

        if (hasOccupancies)
        {
            return OrganizationDeleteCheckResult.NotAllowed(
                "OrganizationContainsOccupancies");
        }

        var hasNetworkSsids =
            await dbContext.NetworkSsids
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.TenantOrganizationId == organizationId,
                    cancellationToken);

        if (hasNetworkSsids)
        {
            return OrganizationDeleteCheckResult.NotAllowed(
                "OrganizationContainsNetworkSsids");
        }

        var hasServiceRequests =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.RequesterOrganizationId == organizationId,
                    cancellationToken);

        if (hasServiceRequests)
        {
            return OrganizationDeleteCheckResult.NotAllowed(
                "OrganizationContainsServiceRequests");
        }

        var hasUserRoles =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.OrganizationId == organizationId,
                    cancellationToken);

        if (hasUserRoles)
        {
            return OrganizationDeleteCheckResult.NotAllowed(
                "OrganizationContainsUserRoles");
        }

        return OrganizationDeleteCheckResult.Allowed();
    }

    public async Task DeleteOrganizationAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var deleteCheck =
            await CanDeleteOrganizationAsync(
                accountId,
                organizationId,
                cancellationToken);

        if (!deleteCheck.CanDelete)
        {
            throw new OrganizationDeleteNotAllowedException(
                deleteCheck.Reason ??
                "OrganizationCannotBeDeleted");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var organization =
            await dbContext.Organizations
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == organizationId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (organization is null)
        {
            throw new InvalidOperationException(
                "Organization was not found in the current account.");
        }

        dbContext.Organizations.Remove(organization);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

