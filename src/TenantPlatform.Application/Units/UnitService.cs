using TenantPlatform.Application.Security;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Application.Units;

public sealed record UnitReferences(bool Children, bool Occupancies, bool Agreements);
public interface IUnitData
{
    Task<List<UnitListItemDto>> ReadListAsync(Guid accountId, Guid? buildingId, CancellationToken cancellationToken);
    Task<UnitDetailsDto?> ReadDetailsAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken);
    Task<IUnitTransaction> BeginAsync(Guid account, CancellationToken ct);
}
public interface IUnitTransaction : IAsyncDisposable
{
    IReadOnlyList<Unit> Units { get; }
    IReadOnlyList<Guid> BuildingIds { get; }
    Task<UnitReferences> ReadReferencesAsync(Guid id, CancellationToken ct);
    Task CommitAsync(Unit unit, bool create, bool delete, CancellationToken ct);
}
public sealed class UnitService(IAuthorizationService authorization, IUnitData data) : IUnitService
{
    private async Task Require(Guid account, string permission, ResourceKind kind, Guid? id, CancellationToken ct)
    {
        if (!await authorization.CanAsync(permission, new(kind, id, account), ct)) throw new UnauthorizedAccessException();
    }
    public async Task<List<UnitListItemDto>> GetUnitsAsync(Guid accountId, Guid? buildingId = null, CancellationToken cancellationToken = default)
    {
        // Existing broad PropertyAdmin list compatibility; never a concrete resource permission.
        await Require(accountId, Permissions.Units.Open, ResourceKind.Account, null, cancellationToken);
        return await data.ReadListAsync(accountId, buildingId, cancellationToken);
    }
    public async Task<UnitDetailsDto?> GetUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Units.Read, ResourceKind.Unit, unitId, cancellationToken);
        return await data.ReadDetailsAsync(accountId, unitId, cancellationToken);
    }
    public async Task<List<UnitListItemDto>> GetParentCandidatesAsync(Guid accountId, Guid buildingId, Guid? excludeUnitId = null, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Units.Create, ResourceKind.Building, buildingId, cancellationToken);
        return (await data.ReadListAsync(accountId, buildingId, cancellationToken)).Where(x => x.Id != excludeUnitId).OrderBy(x => x.Name).ToList();
    }
    public Task<Guid> CreateUnitAsync(Guid accountId, CreateUnitRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync(accountId, null, new Unit { AccountId = accountId, BuildingId = request.BuildingId, ParentUnitId = request.ParentUnitId,
            Name = request.Name.Trim(), Type = request.Type, IsActive = request.IsActive }, cancellationToken);
    public async Task UpdateUnitAsync(Guid accountId, Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken = default) =>
        _ = await SaveAsync(accountId, unitId, new Unit { AccountId = accountId, BuildingId = request.BuildingId, ParentUnitId = request.ParentUnitId,
            Name = request.Name.Trim(), Type = request.Type, IsActive = request.IsActive }, cancellationToken);

    private async Task<Guid> SaveAsync(Guid account, Guid? id, Unit input, CancellationToken ct)
    {
        await Require(account, id.HasValue ? Permissions.Units.Update : Permissions.Units.Create,
            id.HasValue ? ResourceKind.Unit : ResourceKind.Building, id ?? input.BuildingId, ct);
        await Require(account, Permissions.Units.Create, ResourceKind.Building, input.BuildingId, ct);
        await using var transaction = await data.BeginAsync(account, ct);
        await Require(account, id.HasValue ? Permissions.Units.Update : Permissions.Units.Create,
            id.HasValue ? ResourceKind.Unit : ResourceKind.Building, id ?? input.BuildingId, ct);
        await Require(account, Permissions.Units.Create, ResourceKind.Building, input.BuildingId, ct);
        ValidateParent(transaction, input, id);
        var unit = id.HasValue ? transaction.Units.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException()
            : new Unit { Id = Guid.NewGuid(), AccountId = account };
        unit.BuildingId = input.BuildingId; unit.ParentUnitId = input.ParentUnitId; unit.Name = input.Name;
        unit.Type = input.Type; unit.IsActive = input.IsActive;
        await transaction.CommitAsync(unit, !id.HasValue, false, ct);
        return unit.Id;
    }
    private static void ValidateParent(IUnitTransaction transaction, Unit input, Guid? id)
    {
        if (!transaction.BuildingIds.Contains(input.BuildingId)) throw new UnitValidationException("UnitParentNotInSelectedAccount");
        if (!input.ParentUnitId.HasValue) return;
        if (input.ParentUnitId == id) throw new UnitValidationException("UnitCannotBeOwnParent");
        var nodes = transaction.Units.Where(x => x.BuildingId == input.BuildingId).ToDictionary(x => x.Id);
        if (!nodes.TryGetValue(input.ParentUnitId.Value, out var parent)) throw new UnitValidationException("UnitParentNotInSelectedBuilding");
        if (!id.HasValue) return;
        var visited = new HashSet<Guid>();
        while (true)
        {
            if (!visited.Add(parent.Id)) throw new UnitValidationException("UnitHierarchyContainsCircularReference");
            if (parent.Id == id) throw new UnitValidationException("UnitParentCreatesCircularHierarchy");
            if (!parent.ParentUnitId.HasValue) return;
            if (!nodes.TryGetValue(parent.ParentUnitId.Value, out parent))
                throw new InvalidOperationException("The unit hierarchy contains an invalid parent reference.");
        }
    }
    public async Task<UnitDeleteCheckResult> CanDeleteUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Units.Delete, ResourceKind.Unit, unitId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        return CanDelete(await transaction.ReadReferencesAsync(unitId, cancellationToken));
    }
    public async Task DeleteUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Units.Delete, ResourceKind.Unit, unitId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        await Require(accountId, Permissions.Units.Delete, ResourceKind.Unit, unitId, cancellationToken);
        var unit = transaction.Units.SingleOrDefault(x => x.Id == unitId) ?? throw new UnauthorizedAccessException();
        var check = CanDelete(await transaction.ReadReferencesAsync(unitId, cancellationToken));
        if (!check.CanDelete) throw new UnitDeleteNotAllowedException(check.Reason ?? "UnitCannotBeDeleted");
        await transaction.CommitAsync(unit, false, true, cancellationToken);
    }
    private static UnitDeleteCheckResult CanDelete(UnitReferences references) =>
        references.Children ? UnitDeleteCheckResult.NotAllowed("UnitContainsChildUnits") :
        references.Occupancies ? UnitDeleteCheckResult.NotAllowed("UnitContainsOccupancies") :
        references.Agreements ? UnitDeleteCheckResult.NotAllowed("AgreementReferenced") : UnitDeleteCheckResult.Allowed();
}
