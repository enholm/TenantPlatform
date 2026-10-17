using TenantPlatform.Application.Security;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Application.Occupancies;

public sealed record OccupancyState(IReadOnlyList<Unit> Units, IReadOnlyList<Organization> Organizations, IReadOnlyList<Occupancy> Occupancies);
public interface IOccupancyData
{
    Task<List<OccupancyListItemDto>> ReadListAsync(Guid accountId, DateOnly today, Guid? buildingId, Guid? tenantOrganizationId, Guid? unitId, CancellationToken cancellationToken);
    Task<OccupancyDetailsDto?> ReadDetailsAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken);
    Task<Occupancy?> ReadEntityAsync(Guid account, Guid id, CancellationToken ct);
    Task<OccupancyState> ReadHierarchyAsync(Guid account, Guid unitId, CancellationToken ct);
    Task<IOccupancyTransaction> BeginAsync(Guid account, CancellationToken ct);
}
public interface IOccupancyTransaction : IAsyncDisposable
{
    OccupancyState State { get; }
    Task CommitAsync(Occupancy occurrence, bool create, bool delete, CancellationToken ct);
}

public sealed class OccupancyService(IAuthorizationService authorization, IOccupancyData data, TimeProvider clock) : IOccupancyService
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private async Task Require(Guid account, string permission, ResourceKind kind, Guid? id, CancellationToken ct)
    { if (!await authorization.CanAsync(permission, new(kind, id, account), ct)) throw new UnauthorizedAccessException(); }
    public async Task<List<OccupancyListItemDto>> GetOccupanciesAsync(Guid accountId, Guid? buildingId = null, Guid? tenantOrganizationId = null, Guid? unitId = null, CancellationToken cancellationToken = default)
    {
        var today = Today;
        // Existing broad administrative list compatibility, separate from concrete resource access.
        await Require(accountId, Permissions.Occupancies.Open, ResourceKind.Account, null, cancellationToken);
        return await data.ReadListAsync(accountId, today, buildingId, tenantOrganizationId, unitId, cancellationToken);
    }
    public async Task<OccupancyDetailsDto?> GetOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Occupancies.Read, ResourceKind.Occupancy, occupancyId, cancellationToken);
        return await data.ReadDetailsAsync(accountId, occupancyId, cancellationToken);
    }
    public Task<Guid> CreateOccupancyAsync(Guid accountId, CreateOccupancyRequest request, CancellationToken cancellationToken = default) =>
        Save(accountId, null, new Occupancy { AccountId = accountId, TenantOrganizationId = request.TenantOrganizationId, UnitId = request.UnitId,
            ValidFrom = request.ValidFrom, ValidTo = request.ValidTo }, cancellationToken);
    public async Task UpdateOccupancyAsync(Guid accountId, Guid occupancyId, UpdateOccupancyRequest request, CancellationToken cancellationToken = default) =>
        _ = await Save(accountId, occupancyId, new Occupancy { AccountId = accountId, TenantOrganizationId = request.TenantOrganizationId, UnitId = request.UnitId,
            ValidFrom = request.ValidFrom, ValidTo = request.ValidTo }, cancellationToken);
    private async Task<Guid> Save(Guid account, Guid? id, Occupancy input, CancellationToken ct)
    {
        await Require(account, id.HasValue ? Permissions.Occupancies.Update : Permissions.Occupancies.Create,
            id.HasValue ? ResourceKind.Occupancy : ResourceKind.Unit, id ?? input.UnitId, ct);
        await Require(account, Permissions.Occupancies.Create, ResourceKind.Unit, input.UnitId, ct);
        await using var transaction = await data.BeginAsync(account, ct);
        await Require(account, id.HasValue ? Permissions.Occupancies.Update : Permissions.Occupancies.Create,
            id.HasValue ? ResourceKind.Occupancy : ResourceKind.Unit, id ?? input.UnitId, ct);
        await Require(account, Permissions.Occupancies.Create, ResourceKind.Unit, input.UnitId, ct);
        Validate(transaction.State, input, id);
        var entity = id.HasValue ? transaction.State.Occupancies.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException()
            : new Occupancy { Id = Guid.NewGuid(), AccountId = account };
        entity.UnitId = input.UnitId; entity.TenantOrganizationId = input.TenantOrganizationId; entity.ValidFrom = input.ValidFrom; entity.ValidTo = input.ValidTo;
        await transaction.CommitAsync(entity, !id.HasValue, false, ct);
        return entity.Id;
    }
    private static void Validate(OccupancyState state, Occupancy input, Guid? id)
    {
        if (input.ValidTo < input.ValidFrom) throw new OccupancyValidationException("OccupancyValidToBeforeValidFrom");
        var organization = state.Organizations.SingleOrDefault(x => x.Id == input.TenantOrganizationId)
            ?? throw new OccupancyValidationException("OccupancyTenantOrganizationNotFound");
        if (organization.Type != OrganizationType.Tenant) throw new OccupancyValidationException("OccupancyOrganizationMustBeTenant");
        if (!organization.IsActive) throw new OccupancyValidationException("OccupancyTenantOrganizationInactive");
        var unit = state.Units.SingleOrDefault(x => x.Id == input.UnitId) ?? throw new OccupancyValidationException("OccupancyUnitNotFound");
        if (!unit.IsActive) throw new OccupancyValidationException("OccupancyUnitInactive");
        if (state.Occupancies.Any(x => x.Id != id && x.UnitId == input.UnitId && Overlaps(x, input.ValidFrom, input.ValidTo)))
            throw new OccupancyValidationException("OccupancyOverlapsExisting");
    }
    private static bool Overlaps(Occupancy existing, DateOnly from, DateOnly? to) =>
        existing.ValidFrom <= (to ?? DateOnly.MaxValue) && (existing.ValidTo ?? DateOnly.MaxValue) >= from;
    public async Task EndOccupancyAsync(Guid accountId, Guid occupancyId, DateOnly validTo, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Occupancies.End, ResourceKind.Occupancy, occupancyId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        await Require(accountId, Permissions.Occupancies.End, ResourceKind.Occupancy, occupancyId, cancellationToken);
        var entity = transaction.State.Occupancies.SingleOrDefault(x => x.Id == occupancyId) ?? throw new UnauthorizedAccessException();
        Validate(transaction.State, new Occupancy { UnitId = entity.UnitId, TenantOrganizationId = entity.TenantOrganizationId, ValidFrom = entity.ValidFrom, ValidTo = validTo }, occupancyId);
        entity.ValidTo = validTo;
        await transaction.CommitAsync(entity, false, false, cancellationToken);
    }
    public async Task<OccupancyDeleteCheckResult> CanDeleteOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default)
    {
        var today = Today;
        await Require(accountId, Permissions.Occupancies.Delete, ResourceKind.Occupancy, occupancyId, cancellationToken);
        var entity = await data.ReadEntityAsync(accountId, occupancyId, cancellationToken);
        return entity is null ? OccupancyDeleteCheckResult.NotAllowed("OccupancyNotFound") : DeleteRule(entity, today);
    }
    private static OccupancyDeleteCheckResult DeleteRule(Occupancy entity, DateOnly today) =>
        entity.ValidTo < today ? OccupancyDeleteCheckResult.NotAllowed("OccupancyHistoricalCannotBeDeleted") : OccupancyDeleteCheckResult.Allowed();
    public async Task DeleteOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default)
    {
        var today = Today;
        await Require(accountId, Permissions.Occupancies.Delete, ResourceKind.Occupancy, occupancyId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        await Require(accountId, Permissions.Occupancies.Delete, ResourceKind.Occupancy, occupancyId, cancellationToken);
        var entity = transaction.State.Occupancies.SingleOrDefault(x => x.Id == occupancyId) ?? throw new UnauthorizedAccessException();
        var check = DeleteRule(entity, today);
        if (!check.CanDelete) throw new OccupancyDeleteNotAllowedException(check.Reason ?? "OccupancyCannotBeDeleted");
        await transaction.CommitAsync(entity, false, true, cancellationToken);
    }
    public async Task<List<OccupancyHierarchyConflictDto>> GetHierarchyConflictsAsync(Guid accountId, Guid unitId, DateOnly validFrom, DateOnly? validTo, Guid? excludeOccupancyId = null, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Occupancies.Create, ResourceKind.Unit, unitId, cancellationToken);
        var state = await data.ReadHierarchyAsync(accountId, unitId, cancellationToken);
        var units = state.Units.ToDictionary(x => x.Id);
        if (!units.TryGetValue(unitId, out var target)) throw new UnauthorizedAccessException();
        var ancestors = new HashSet<Guid>(); var parent = target.ParentUnitId;
        while (parent.HasValue && ancestors.Add(parent.Value) && units.TryGetValue(parent.Value, out var node)) parent = node.ParentUnitId;
        var children = state.Units.ToLookup(x => x.ParentUnitId);
        var descendants = new HashSet<Guid>(); var pending = new Queue<Guid>(); pending.Enqueue(unitId);
        while (pending.TryDequeue(out var next)) foreach (var child in children[next]) if (descendants.Add(child.Id)) pending.Enqueue(child.Id);
        var organizations = state.Organizations.ToDictionary(x => x.Id, x => x.Name);
        return state.Occupancies.Where(x => x.Id != excludeOccupancyId && (ancestors.Contains(x.UnitId) || descendants.Contains(x.UnitId)) && Overlaps(x, validFrom, validTo))
            .Select(x => new OccupancyHierarchyConflictDto { OccupancyId = x.Id, UnitId = x.UnitId, UnitName = units[x.UnitId].Name,
                TenantOrganizationName = organizations.GetValueOrDefault(x.TenantOrganizationId, ""), ValidFrom = x.ValidFrom, ValidTo = x.ValidTo,
                ConflictType = ancestors.Contains(x.UnitId) ? OccupancyHierarchyConflictType.ParentUnit : OccupancyHierarchyConflictType.ChildUnit })
            .OrderBy(x => x.UnitName).ToList();
    }
}
