using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record EquipmentPage(List<LeasingEquipment> Rows, int Count, Dictionary<Guid,string> Acquisitions, Dictionary<Guid,string> Buildings, int Page);
public sealed record LeasingEquipmentIdentity(Guid Id, Guid ItemId, string? InternalId, string? SerialNumber);
public sealed record LeasingEquipmentState(LeasingAcquisition? Acquisition, LeasingFramework? Framework, LeasingLifecycle? Lifecycle,
    List<LeasingEquipment> Equipment, List<LeasingDisposition> Dispositions, List<LeasingEquipmentIdentity> AccountIdentities,
    List<Guid> Buildings, List<Guid> Documents, LeasingPaymentEvent? PreviousRequest);
public interface ILeasingEquipmentData
{
    Task<List<LeasingOption>> ReadBuildingsAsync(Guid account, CancellationToken ct);
    Task<EquipmentPage> ReadPageAsync(LeasingUserScope scope, string? search, LeasingEquipmentStatus? status, Guid? acquisition, int page, CancellationToken ct);
    Task<ILeasingEquipmentWork> BeginAsync(Guid account, Guid acquisition, Guid? request, CancellationToken ct);
}
public interface ILeasingEquipmentWork : IAsyncDisposable
{
    LeasingEquipmentState State { get; }
    Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct);
    Task CommitAsync(LeasingPaymentEvent history, CancellationToken ct);
}
public sealed class LeasingEquipmentService(IAuthorizationService authorization, ILeasingEquipmentData data, TimeProvider clock)
{
    public async Task<List<LeasingOption>> EquipmentBuildingsAsync(Guid account, CancellationToken ct = default)
    {
        await authorization.GetAccountActorScopeAsync(account, ct); return await data.ReadBuildingsAsync(account, ct);
    }
    public async Task<EquipmentPage> ListEquipmentAsync(Guid account, string? search = null, LeasingEquipmentStatus? status = null, Guid? acquisition = null, int page = 1, CancellationToken ct = default)
    {
        var scope = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct)); return await data.ReadPageAsync(scope, search, status, acquisition, page, ct);
    }
    private async Task<AccountActorScope> Maintain(Guid account, CancellationToken ct)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!LeasingUserAccess.Has(actor, Permissions.Leasing.MaintainEquipment)) throw new UnauthorizedAccessException(); return actor;
    }
    private async Task<AccountActorScope> Revalidate(AccountActorScope actor, LeasingEquipmentState state, CancellationToken ct)
    {
        var now = await Maintain(actor.AccountId, ct); if (now.UserId != actor.UserId) throw new UnauthorizedAccessException();
        var scope = LeasingUserAccess.Create(now);
        if (state.Acquisition is null || !scope.CanReadLifecycle && state.Acquisition.OwnerUserId != now.UserId && state.Framework?.OwnerUserId != now.UserId) throw new UnauthorizedAccessException(); return now;
    }
    public async Task SetCountableAsync(Guid account, Guid acquisition, Guid item, bool countable, string reason, CancellationToken ct = default)
    {
        var actor = await Maintain(account, ct); await using var work = await data.BeginAsync(account, acquisition, null, ct); actor = await Revalidate(actor, work.State, ct);
        Reason(reason); var state = work.State; var line = state.Acquisition!.Items.SingleOrDefault(x => x.Id == item) ?? throw new UnauthorizedAccessException();
        if (countable && line.Quantity != decimal.Truncate(line.Quantity) || !countable && (state.AccountIdentities.Any(x => x.ItemId == item) || state.Dispositions.Any(x => x.ItemId == item))) throw new LeasingValidationException("LifeCountableRequired");
        var before = JsonSerializer.Serialize(new { line.Id, line.CountableEquipment }); line.CountableEquipment = countable;
        await work.CommitAsync(History(account, acquisition, actor.UserId, "EquipmentCountable", reason, before, JsonSerializer.Serialize(new { line.Id, line.CountableEquipment }), Guid.NewGuid()), ct);
    }
    public async Task<List<Guid>> SaveEquipmentAsync(Guid account, Guid acquisition, List<LeasingEquipment> inputs, bool duplicateSerialConfirmed, string reason, Guid request, CancellationToken ct = default)
    {
        inputs = JsonSerializer.Deserialize<List<LeasingEquipment>>(JsonSerializer.Serialize(inputs))!;
        var actor = await Maintain(account, ct); await using var work = await data.BeginAsync(account, acquisition, request, ct); actor = await Revalidate(actor, work.State, ct);
        var state = work.State; var a = state.Acquisition!; Reason(reason);
        if (request == Guid.Empty) throw new LeasingValidationException("PaymentInvalid");
        if (state.PreviousRequest is { } old)
        {
            if (old.ActorUserId != actor.UserId || old.Action != "EquipmentSaved" || old.AcquisitionId != acquisition || old.InvoiceId is not null) throw new LeasingValidationException("LeasingConcurrency");
            return JsonSerializer.Deserialize<List<LeasingEquipment>>(old.AfterJson)!.Select(x => x.Id).ToList();
        }
        if (inputs.Where(x => x.Id != Guid.Empty).Select(x => x.Id).Distinct().Count() != inputs.Count(x => x.Id != Guid.Empty) || inputs.Count is < 1 or > 1000 || a.Status != LeasingAcquisitionStatus.Registered || state.Lifecycle?.Status == LeasingLifecycleStatus.Closed) throw new LeasingValidationException("LifeClosed");
        var before = new List<LeasingEquipment>(); var result = new List<LeasingEquipment>();
        foreach (var input in inputs)
        {
            if (input.ItemId == Guid.Empty) throw new LeasingValidationException("LifeSelectEquipmentItem");
            var item = a.Items.SingleOrDefault(x => x.Id == input.ItemId) ?? throw new UnauthorizedAccessException();
            if (!item.CountableEquipment) throw new LeasingValidationException("LifeCountableRequired");
            if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 500 || input.Notes.Length > 10000 || input.SerialNumber?.Length > 200 || input.InternalId?.Length > 100 || input.RegisteredDate == default || input.Status is not (LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn)) throw new LeasingValidationException("LifeInvalidEquipment");
            input.InternalId = string.IsNullOrWhiteSpace(input.InternalId) ? null : input.InternalId.Trim().ToUpperInvariant(); input.SerialNumber = string.IsNullOrWhiteSpace(input.SerialNumber) ? null : input.SerialNumber.Trim();
            if (input.OwnerUserId.HasValue && !LeasingLifecycleRecipientRules.CanRead(await work.ReadRecipientAsync(input.OwnerUserId.Value, ct))) throw new LeasingValidationException("LifeInvalidOwner");
            if (input.SourceDocumentId.HasValue && !state.Documents.Contains(input.SourceDocumentId.Value) || input.BuildingId.HasValue && !state.Buildings.Contains(input.BuildingId.Value)) throw new UnauthorizedAccessException();
            var unit = input.Id == Guid.Empty ? new LeasingEquipment { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = acquisition, ItemId = item.Id } : state.Equipment.SingleOrDefault(x => x.Id == input.Id) ?? throw new UnauthorizedAccessException();
            if (input.Id != Guid.Empty)
            {
                if (unit.Revision != input.Revision) throw new LeasingValidationException("LeasingConcurrency");
                if (unit.ItemId != item.Id || unit.Status is not (LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn)) throw new LeasingValidationException("LifeEventRequired");
                before.Add(JsonSerializer.Deserialize<LeasingEquipment>(JsonSerializer.Serialize(unit))!);
            }
            else
            {
                var existing = state.AccountIdentities.Count(x => x.ItemId == item.Id); var untracked = state.Dispositions.Where(x => x.ItemId == item.Id && !x.Reversed && x.EquipmentId is null).Sum(x => x.Quantity);
                if (existing + untracked + result.Count(x => x.ItemId == item.Id && inputs.All(i => i.Id != x.Id)) + 1 > item.Quantity) throw new LeasingValidationException("LifeOverQuantity");
                state.Equipment.Add(unit);
            }
            if (input.InternalId != null && (result.Any(x => x.InternalId == input.InternalId) || state.AccountIdentities.Any(x => x.Id != unit.Id && x.InternalId == input.InternalId))) throw new LeasingValidationException("LifeDuplicateInternalId");
            if (!duplicateSerialConfirmed && input.SerialNumber != null && (result.Any(x => string.Equals(x.SerialNumber, input.SerialNumber, StringComparison.OrdinalIgnoreCase)) || state.AccountIdentities.Any(x => x.Id != unit.Id && x.SerialNumber != null && x.SerialNumber.ToUpper() == input.SerialNumber.ToUpper()))) throw new LeasingValidationException("LifeDuplicateSerial");
            unit.InternalId = input.InternalId; unit.SerialNumber = input.SerialNumber; unit.Description = input.Description; unit.BuildingId = input.BuildingId; unit.OwnerUserId = input.OwnerUserId; unit.Status = input.Status; unit.StatusDate = input.StatusDate; unit.RegisteredDate = input.RegisteredDate; unit.SourceDocumentId = input.SourceDocumentId; unit.Notes = input.Notes; unit.Revision = Guid.NewGuid(); result.Add(unit);
        }
        await work.CommitAsync(History(account, acquisition, actor.UserId, "EquipmentSaved", reason, JsonSerializer.Serialize(before), JsonSerializer.Serialize(result), request), ct); return result.Select(x => x.Id).ToList();
    }
    private static void Reason(string reason) { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired"); }
    private LeasingPaymentEvent History(Guid account, Guid acquisition, Guid actor, string action, string reason, string before, string after, Guid request) => new() { Id = request, AccountId = account, AcquisitionId = acquisition, ActorUserId = actor, RecordedUtc = clock.GetUtcNow(), Action = action, Reason = reason, BeforeJson = before, AfterJson = after };
}
