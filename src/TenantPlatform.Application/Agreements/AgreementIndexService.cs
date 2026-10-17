using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed record AgreementIndexState(List<AgreementIndex> Indices, List<AgreementIndexValue> Values, List<AgreementAdjustmentProposal> PendingProposals);
public interface IAgreementIndexData
{
    Task<AgreementIndexState> ReadAsync(Guid account, CancellationToken ct);
    Task<IAgreementIndexTransaction> BeginAsync(Guid account, bool includeProposals, CancellationToken ct);
}
public interface IAgreementIndexTransaction : IAsyncDisposable
{
    AgreementIndexState State { get; }
    Task CommitAsync(CancellationToken ct);
}
public sealed class AgreementIndexService(IAuthorizationService authorization, IAgreementIndexData data, TimeProvider clock)
{
    private async Task<Guid> Require(Guid account, CancellationToken ct)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!actor.Assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Agreements.Create) && !x.OrganizationId.HasValue && !x.BuildingId.HasValue)) throw new UnauthorizedAccessException();
        return actor.UserId;
    }
    public async Task<AgreementIndexRegister> GetIndicesAsync(Guid accountId, CancellationToken ct = default)
    {
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        var state = await data.ReadAsync(accountId, ct);
        return new(scope.CanReadAll, state.Indices.OrderBy(x => x.Code).ToList(), state.Values.OrderByDescending(x => x.Period).ThenByDescending(x => x.Revision).ToList());
    }
    private static void Validate(string code, string name, string description, string source, AgreementIndexResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || string.IsNullOrWhiteSpace(name) || name.Length > 200 || description.Length > 4000 || string.IsNullOrWhiteSpace(source) || source.Length > 2000 || !Enum.IsDefined(resolution)) throw new AgreementValidationException("ProcessingInvalidIndex");
    }
    public async Task<Guid> CreateIndexAsync(Guid accountId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default)
    {
        var initial = await Require(accountId, ct); Validate(code, name, description, source, resolution);
        await using var tx = await data.BeginAsync(accountId, false, ct);
        if (await Require(accountId, ct) != initial) throw new UnauthorizedAccessException();
        if (tx.State.Indices.Any(x => x.Code == code.Trim().ToUpperInvariant())) throw new AgreementValidationException("SimpleDuplicateIndex");
        var index = new AgreementIndex { Id = Guid.NewGuid(), AccountId = accountId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description.Trim(), Source = source.Trim(), Resolution = resolution, RecordedUtc = clock.GetUtcNow(), ActorUserId = initial };
        tx.State.Indices.Add(index); await tx.CommitAsync(ct); return index.Id;
    }
    public async Task UpdateIndexAsync(Guid accountId, Guid indexId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default)
    {
        var initial = await Require(accountId, ct);
        await using var tx = await data.BeginAsync(accountId, false, ct);
        if (await Require(accountId, ct) != initial) throw new UnauthorizedAccessException();
        var index = tx.State.Indices.SingleOrDefault(x => x.Id == indexId) ?? throw new UnauthorizedAccessException();
        Validate(code, name, description, source, resolution);
        if (tx.State.Indices.Any(x => x.Id != indexId && x.Code == code.Trim().ToUpperInvariant())) throw new AgreementValidationException("SimpleDuplicateIndex");
        if (tx.State.Values.Any(x => x.IndexId == indexId && !x.Superseded && AgreementAdjustmentCalculator.Period(x.Period, resolution) != x.Period)) throw new AgreementValidationException("ProcessingInvalidIndex");
        index.Code = code.Trim().ToUpperInvariant(); index.Name = name.Trim(); index.Description = description.Trim(); index.Source = source.Trim(); index.Resolution = resolution; index.RecordedUtc = clock.GetUtcNow(); index.ActorUserId = initial;
        await tx.CommitAsync(ct);
    }
    public async Task RecordIndexValueAsync(Guid accountId, Guid indexId, DateOnly period, decimal value, DateOnly? published, string reason, Guid? originalValueId = null, CancellationToken ct = default)
    {
        var initial = await Require(accountId, ct);
        await using var tx = await data.BeginAsync(accountId, true, ct);
        if (await Require(accountId, ct) != initial) throw new UnauthorizedAccessException();
        var index = tx.State.Indices.SingleOrDefault(x => x.Id == indexId) ?? throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new AgreementValidationException("ProcessingReasonRequired");
        if (value <= 0 || value > 100000000 || decimal.Round(value, 8) != value || period == default || period.Year > 9997 || AgreementAdjustmentCalculator.Period(period, index.Resolution) != period) throw new AgreementValidationException("ProcessingInvalidIndex");
        var values = tx.State.Values.Where(x => x.IndexId == indexId).ToList();
        var revision = values.Where(x => x.Period == period).Select(x => x.Revision).DefaultIfEmpty().Max(); var periodKey = Guid.NewGuid();
        if (!originalValueId.HasValue && revision > 0) throw new AgreementValidationException("SimpleDuplicatePeriod");
        if (originalValueId.HasValue)
        {
            var original = values.SingleOrDefault(x => x.Id == originalValueId) ?? throw new UnauthorizedAccessException();
            periodKey = original.PeriodKey; revision = Math.Max(revision, original.Revision);
            if (original.Superseded || values.Any(x => x.PeriodKey == periodKey && x.Revision > original.Revision)) throw new AgreementValidationException("ProcessingStale");
            if (original.Period != period && values.Any(x => x.Period == period && x.PeriodKey != periodKey)) throw new AgreementValidationException("SimpleDuplicatePeriod");
            foreach (var old in values.Where(x => x.PeriodKey == periodKey && !x.Superseded)) old.Superseded = true;
        }
        foreach (var proposal in tx.State.PendingProposals)
        {
            var calculation = JsonSerializer.Deserialize<AgreementAdjustmentCalculation>(proposal.CalculationJson)!;
            if (calculation.BaseIndex?.IndexId == indexId || calculation.ComparisonIndex?.IndexId == indexId) proposal.Status = AgreementProposalStatus.Stale;
        }
        tx.State.Values.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, IndexId = indexId, Period = period, Value = value, PeriodKey = periodKey, Revision = revision + 1,
            PublishedDate = published, Reason = reason.Trim(), RecordedUtc = clock.GetUtcNow(), ActorUserId = initial });
        await tx.CommitAsync(ct);
    }
}
