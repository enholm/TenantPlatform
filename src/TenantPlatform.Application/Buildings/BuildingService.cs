using TenantPlatform.Application.Security;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Application.Buildings;

public sealed record BuildingReferences(bool Units, bool Requests, bool Networks, bool MeetingRooms, bool Agreements);
public interface IBuildingData
{
    Task<List<BuildingListItemDto>> ReadListAsync(Guid accountId, CancellationToken cancellationToken);
    Task<BuildingDetailsDto?> ReadDetailsAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken);
    Task<IBuildingTransaction> BeginAsync(Guid accountId, Guid? id, CancellationToken ct);
}
public interface IBuildingTransaction : IAsyncDisposable
{
    Building Building { get; }
    Task<BuildingReferences> ReadReferencesAsync(CancellationToken ct);
    Task CommitAsync(bool delete, CancellationToken ct);
}

public sealed class BuildingService(IAuthorizationService authorization, IBuildingData data) : IBuildingService
{
    private async Task Require(Guid account, string permission, Guid? id, CancellationToken ct)
    {
        if (!await authorization.CanAsync(permission, new(id.HasValue ? ResourceKind.Building : ResourceKind.Account, id, account), ct))
            throw new UnauthorizedAccessException();
    }

    public async Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        // Explicit legacy list exception: PropertyAdmin sees the account's building list.
        // This does not authorize opening or changing any listed resource.
        await Require(accountId, Permissions.Buildings.Open, null, cancellationToken);
        return await data.ReadListAsync(accountId, cancellationToken);
    }

    public async Task<BuildingDetailsDto?> GetBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Buildings.Read, buildingId, cancellationToken);
        return await data.ReadDetailsAsync(accountId, buildingId, cancellationToken);
    }

    public async Task<Guid> CreateBuildingAsync(Guid accountId, CreateBuildingRequest request, CancellationToken cancellationToken = default)
    {
        var input = new Building { Name = request.Name.Trim(), AddressLine1 = request.AddressLine1?.Trim(),
            PostalCode = request.PostalCode?.Trim(), City = request.City?.Trim(), CountryCode = request.CountryCode.Trim().ToUpperInvariant() };
        await Require(accountId, Permissions.Buildings.Create, null, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, null, cancellationToken);
        await Require(accountId, Permissions.Buildings.Create, null, cancellationToken);
        Apply(transaction.Building, input);
        await transaction.CommitAsync(false, cancellationToken);
        return transaction.Building.Id;
    }

    public async Task UpdateBuildingAsync(Guid accountId, Guid buildingId, UpdateBuildingRequest request, CancellationToken cancellationToken = default)
    {
        var input = new Building { Name = request.Name.Trim(), AddressLine1 = request.AddressLine1?.Trim(),
            PostalCode = request.PostalCode?.Trim(), City = request.City?.Trim(), CountryCode = request.CountryCode.Trim().ToUpperInvariant() };
        await Require(accountId, Permissions.Buildings.Update, buildingId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, buildingId, cancellationToken);
        await Require(accountId, Permissions.Buildings.Update, buildingId, cancellationToken);
        Apply(transaction.Building, input);
        await transaction.CommitAsync(false, cancellationToken);
    }

    public async Task<BuildingDeleteCheckResult> CanDeleteBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Buildings.Delete, buildingId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, buildingId, cancellationToken);
        return CanDelete(await transaction.ReadReferencesAsync(cancellationToken));
    }

    public async Task DeleteBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.Buildings.Delete, buildingId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, buildingId, cancellationToken);
        await Require(accountId, Permissions.Buildings.Delete, buildingId, cancellationToken);
        var check = CanDelete(await transaction.ReadReferencesAsync(cancellationToken));
        if (!check.CanDelete) throw new BuildingDeleteNotAllowedException(check.Reason ?? "BuildingCannotBeDeleted");
        await transaction.CommitAsync(true, cancellationToken);
    }

    private static BuildingDeleteCheckResult CanDelete(BuildingReferences references) =>
        references.Units ? BuildingDeleteCheckResult.NotAllowed("BuildingContainsUnits") :
        references.Requests ? BuildingDeleteCheckResult.NotAllowed("BuildingContainsServiceRequests") :
        references.Networks ? BuildingDeleteCheckResult.NotAllowed("BuildingContainsNetworkEnvironments") :
        references.MeetingRooms ? BuildingDeleteCheckResult.NotAllowed("BuildingContainsMeetingRooms") :
        references.Agreements ? BuildingDeleteCheckResult.NotAllowed("AgreementReferenced") : BuildingDeleteCheckResult.Allowed();

    private static void Apply(Building target, Building input)
    {
        target.Name = input.Name; target.AddressLine1 = input.AddressLine1;
        target.PostalCode = input.PostalCode; target.City = input.City; target.CountryCode = input.CountryCode;
    }
}
