using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.AccountSettings;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    private static async Task<(List<AgreementOptionDto> Elements, List<AgreementOptionDto> Areas)> GetStructureOptionsAsync(
        TenantPlatformDbContext db, Guid accountId, Agreement? current, bool includeInactive, CancellationToken ct)
    {
        // Include inactive ancestors when building paths; only selectable rows are filtered out.
        var elements = await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct);
        var areas = await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct);
        var elementStates = elements.ToDictionary(x => x.Id, x => x.IsActive);
        var areaStates = areas.ToDictionary(x => x.Id, x => x.IsActive);
        var elementOptions = AccountRegisterHierarchy.Build(elements.Select(x => (x.Id, x.ParentId, x.Name)))
            .Where(x => includeInactive || elementStates[x.Id] || x.Id == current?.OrganizationElementId)
            .Select(x => new AgreementOptionDto(x.Id, x.Path, IsActive: elementStates[x.Id])).ToList();
        var areaOptions = AccountRegisterHierarchy.Build(areas.Select(x => (x.Id, x.ParentId, x.Name)))
            .Where(x => includeInactive || areaStates[x.Id] || x.Id == current?.GeographicAreaId)
            .Select(x => new AgreementOptionDto(x.Id, x.Path, IsActive: areaStates[x.Id])).ToList();
        return (elementOptions, areaOptions);
    }

    private static async Task ValidateStructureAsync(TenantPlatformDbContext db, Guid accountId,
        SaveAgreementRequest request, Agreement? existing, CancellationToken ct)
    {
        if (request.OrganizationElementId.HasValue && !await db.OrganizationElements.AnyAsync(x =>
            x.AccountId == accountId && x.Id == request.OrganizationElementId &&
            (x.IsActive || x.Id == (existing == null ? null : existing.OrganizationElementId)), ct))
            throw new AgreementValidationException("AgreementInvalidOrganizationElement");
        if (request.GeographicAreaId.HasValue && !await db.GeographicAreas.AnyAsync(x =>
            x.AccountId == accountId && x.Id == request.GeographicAreaId &&
            (x.IsActive || x.Id == (existing == null ? null : existing.GeographicAreaId)), ct))
            throw new AgreementValidationException("AgreementInvalidGeographicArea");
    }
}
