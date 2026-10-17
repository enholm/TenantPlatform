using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingOrderWriter(IAuthorizationService authorization, ILeasingOrderWriteData data, TimeProvider clock)
{
    private async Task<AccountActorScope> Actor(Guid account, CancellationToken ct) => await authorization.GetAccountActorScopeAsync(account, ct);
    private async Task<AccountActorScope> Revalidate(AccountActorScope before, CancellationToken ct) { var now = await Actor(before.AccountId, ct); if (now.UserId != before.UserId) throw new UnauthorizedAccessException(); return now; }
    private static bool Owns(LeasingUserScope actor, LeasingOrder order, LeasingFramework framework) => actor.IsAdministrator || order.OwnerUserId == actor.UserId || framework.OwnerUserId == actor.UserId;
    private static bool Retry(LeasingOrderEvent? previous, Guid order, Guid request, Guid user, string action)
    {
        if (request == Guid.Empty) throw new LeasingValidationException("LeasingOrderInvalid");
        if (previous is null) return false;
        if (previous.OrderId != order || previous.ActorUserId != user || previous.Action != action) throw new LeasingValidationException("LeasingConcurrency"); return true;
    }
    private static T Copy<T>(T entity) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(entity))!;
    private async Task ValidateOrder(ILeasingOrderWork work, Guid account, LeasingOrder input, LeasingOrder? original, CancellationToken ct)
    {
        var references = await work.ReferencesAsync(input.SupplierOrganizationId, input.OwnerUserId, ct);
        if (!references.Supplier) throw new LeasingValidationException("LeasingInvalidParty");
        if (!references.Owner) throw new LeasingValidationException("LeasingInvalidOwner");
        if (input.OrderDate == default || input.ExpectedDate == DateOnly.MinValue || input.Notes?.Length > 10000 || input.Lines.Count is < 1 or > 500) throw new LeasingValidationException("LeasingOrderInvalid");
        if (original?.Lines.Any(x => x.Fulfilled + x.Unreserved > 0) == true && original.SupplierOrganizationId != input.SupplierOrganizationId) throw new LeasingValidationException("LeasingOrderInvalid");
        var catalog = work.State.Catalog;
        var ids = new HashSet<Guid>();
        foreach (var line in input.Lines)
        {
            if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
            if (!ids.Add(line.Id) || string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500 || line.ItemNumber?.Length > 100 || string.IsNullOrWhiteSpace(line.Unit) || line.Unit.Length > 30 ||
                !Enum.IsDefined(line.Method) || line.Quantity <= 0 || line.Quantity > 1000000000 || line.UnitPrice < 0 || line.UnitPrice > 1000000000000 || line.VatPercent is < 0 or > 100 ||
                !Precision(line.Quantity, 4) || !Precision(line.UnitPrice, 4) || !Precision(line.VatPercent, 4)) throw new LeasingValidationException("LeasingOrderInvalid");
            var old = original?.Lines.SingleOrDefault(x => x.Id == line.Id);
            line.Fulfilled = old?.Fulfilled ?? 0; line.Unreserved = old?.Unreserved ?? 0;
            if (line.Scope < line.Fulfilled + line.Unreserved || old != null && (old.Fulfilled + old.Unreserved > 0) && line.Method != old.Method) throw new LeasingValidationException("LeasingOrderOverDelivery");
            var classification = JsonSerializer.Deserialize<LeasingClassificationInput>(line.ClassificationJson) ?? new();
            // Reuse phase-two validation on a detached scratch graph; do not persist acquisition rows for an order.
            var item = line.Item(); item.Id = Guid.NewGuid(); item.AccountId = account;
            LeasingClassificationRules.Apply(item, classification.Copy(), catalog, false);
            line.ClassificationJson = Snapshot(classification.Copy());
        }
        if (input.Lines.Sum(x => LeasingCalculator.Line(x.Item()).Gross) > 1000000000000m || original?.Lines.Any(x => x.Fulfilled + x.Unreserved > 0 && !ids.Contains(x.Id)) == true) throw new LeasingValidationException("LeasingOrderInvalid");
    }
    private static void CopyOrder(LeasingOrder target, LeasingOrder input)
    {
        target.SupplierOrganizationId = input.SupplierOrganizationId; target.OwnerUserId = input.OwnerUserId;
        target.OrderDate = input.OrderDate; target.ExpectedDate = input.ExpectedDate; target.Notes = input.Notes;
    }
    private static void CopyLines(LeasingOrder target, LeasingOrder input)
    {
        foreach (var old in target.Lines.Where(x => input.Lines.All(n => n.Id != x.Id)).ToList()) { target.Lines.Remove(old); }
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var source = input.Lines[i]; var line = target.Lines.SingleOrDefault(x => x.Id == source.Id);
            if (line == null) { line = new() { Id = source.Id, AccountId = target.AccountId, OrderId = target.Id }; target.Lines.Add(line); }
            line.Position = i; line.Description = source.Description; line.ItemNumber = source.ItemNumber; line.Unit = source.Unit;
            line.Quantity = source.Quantity; line.UnitPrice = source.UnitPrice; line.VatPercent = source.VatPercent; line.Method = source.Method; line.ClassificationJson = source.ClassificationJson;
        }
    }
    public async Task<Guid> SaveOrderAsync(Guid account, Guid? id, LeasingOrder input, string reason, Guid request, CancellationToken ct = default)
    {
        input = Copy(input); var actor = await Actor(account, ct);
        if (input.FrameworkId == Guid.Empty) throw new LeasingValidationException("OrderFrameworkRequired");
        await using var work = await data.BeginAsync(account, id ?? request, input.FrameworkId, null, request, ct); actor = await Revalidate(actor, ct);
        var scope = LeasingUserAccess.Create(actor); var state = work.State; var f = state.Framework ?? throw new UnauthorizedAccessException(); var oid = id ?? request;
        var o = id.HasValue ? state.Order ?? throw new UnauthorizedAccessException() : state.Order ?? new LeasingOrder { Id = oid, AccountId = account, FrameworkId = input.FrameworkId, Status = LeasingOrderStatus.Draft };
        if (state.Order is not null && (state.OriginalFramework is null || !Owns(scope, o, state.OriginalFramework)) || !scope.IsAdministrator && f.OwnerUserId != actor.UserId && !(id.HasValue && o.FrameworkId == f.Id && o.OwnerUserId == actor.UserId)) throw new UnauthorizedAccessException();
        if (Retry(state.PreviousEvent, oid, request, actor.UserId, "Saved")) return oid;
        if (!id.HasValue && state.Order is not null) throw new LeasingValidationException("LeasingConcurrency");
        CheckRevision(o.Revision, input.Revision, id.HasValue); Reason(reason, id.HasValue);
        if (id.HasValue && o.FrameworkId != input.FrameworkId || input.Currency != f.Currency || o.Status is LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed or LeasingOrderStatus.Pending) throw new LeasingValidationException("LeasingInvalidTransition");
        if (!scope.IsAdministrator && id.HasValue && o.OwnerUserId != input.OwnerUserId) throw new UnauthorizedAccessException();
        await ValidateOrder(work, account, input, o, ct); var before = Snapshot(o); o.ProposedByUserId = actor.UserId;
        if (o.Status == LeasingOrderStatus.Approved) o.ProposalJson = Snapshot(input);
        else { CopyOrder(o, input); CopyLines(o, input); o.Currency = f.Currency; o.Status = LeasingOrderStatus.Draft; }
        o.Revision = Guid.NewGuid(); if (!id.HasValue) o.Number = $"B-{state.OrderCount + 1:000000}";
        await work.SaveOrderAsync(o, Event(o, actor.UserId, "Saved", reason, before, request), ct); await work.CommitAsync(ct); return o.Id;
    }
    public async Task OrderActionAsync(Guid account, Guid id, Guid revision, LeasingOrderStatus status, string reason, Guid request, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var actor = await Actor(account, ct);
        await using var work = await data.BeginAsync(account, id, null, null, request, ct); actor = await Revalidate(actor, ct);
        var scope = LeasingUserAccess.Create(actor); var o = work.State.Order ?? throw new UnauthorizedAccessException(); var f = work.State.Framework ?? throw new UnauthorizedAccessException();
        if (!scope.CanReadOrders && !Owns(scope, o, f)) throw new UnauthorizedAccessException();
        // Revalidate the action authority before accepting an idempotent retry.
        if (status is LeasingOrderStatus.Approved or LeasingOrderStatus.Rejected) { if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveOrders)) throw new UnauthorizedAccessException(); }
        else if (!Owns(scope, o, f)) throw new UnauthorizedAccessException();
        if (Retry(work.State.PreviousEvent, id, request, actor.UserId, status.ToString())) return;
        CheckRevision(o.Revision, revision, true); Reason(reason, true); var before = Snapshot(o);
        if (status is LeasingOrderStatus.Approved or LeasingOrderStatus.Rejected)
        {
            if (o.Status != LeasingOrderStatus.Pending && !(o.Status == LeasingOrderStatus.Approved && o.ProposalJson != null)) throw new LeasingValidationException("LeasingInvalidTransition");
            if (status == LeasingOrderStatus.Approved)
            {
                if (f.Status != LeasingFrameworkStatus.Open || !LeasingCalculator.InPeriod(AgreementReminderSchedule.Today(now, work.State.TimeZone), f.AcquisitionFrom, f.AcquisitionTo)) throw new LeasingValidationException("LeasingOutsidePeriod");
                if (o.ProposalJson != null) { var proposal = JsonSerializer.Deserialize<LeasingOrder>(o.ProposalJson)!; await ValidateOrder(work, account, proposal, o, ct); CopyOrder(o, proposal); CopyLines(o, proposal); }
                o.Status = status; o.ApprovedByUserId = actor.UserId; o.ApprovedUtc = now;
            }
            else if (o.ProposalJson == null) o.Status = status;
            o.ProposalJson = null;
        }
        else
        {
            var valid = status == LeasingOrderStatus.Pending && o.Status is LeasingOrderStatus.Draft or LeasingOrderStatus.Rejected || status is LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed && o.Status is not (LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed);
            if (!valid) throw new LeasingValidationException("LeasingInvalidTransition"); o.Status = status; o.ProposalJson = null;
        }
        o.Revision = Guid.NewGuid(); await work.SaveOrderAsync(o, Event(o, actor.UserId, status.ToString(), reason, before, request), ct);
        await Capacity(work, ct); await work.CommitAsync(ct);
    }
    public async Task ReopenOrderAsync(Guid account, Guid id, Guid revision, string reason, Guid request, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var actor = await Actor(account, ct);
        if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveOrders)) throw new UnauthorizedAccessException();
        await using var work = await data.BeginAsync(account, id, null, null, request, ct); actor = await Revalidate(actor, ct);
        if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveOrders)) throw new UnauthorizedAccessException();
        var o = work.State.Order ?? throw new UnauthorizedAccessException(); var f = work.State.Framework ?? throw new UnauthorizedAccessException();
        if (Retry(work.State.PreviousEvent, id, request, actor.UserId, "Reopened")) return;
        CheckRevision(o.Revision, revision, true); Reason(reason, true);
        if (o.Status != LeasingOrderStatus.Approved || o.Lines.All(x => x.Unreserved == 0)) throw new LeasingValidationException("LeasingInvalidTransition");
        if (f.Status != LeasingFrameworkStatus.Open || !LeasingCalculator.InPeriod(AgreementReminderSchedule.Today(now, work.State.TimeZone), f.AcquisitionFrom, f.AcquisitionTo)) throw new LeasingValidationException("LeasingOutsidePeriod");
        var before = Snapshot(o); foreach (var line in o.Lines) line.Unreserved = 0; o.Revision = Guid.NewGuid();
        await work.SaveOrderAsync(o, Event(o, actor.UserId, "Reopened", reason, before, request), ct); await Capacity(work, ct); await work.CommitAsync(ct);
    }
    public async Task ProposeLimitAsync(Guid account, Guid framework, Guid revision, decimal limit, string reason, string? document, Guid request, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct); if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveLimit)) throw new UnauthorizedAccessException();
        await using var work = await data.BeginAsync(account, null, framework, request, null, ct); actor = await Revalidate(actor, ct);
        if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveLimit)) throw new UnauthorizedAccessException();
        if (work.State.Change is { } old) { if (old.ProposedByUserId == actor.UserId && old.FrameworkId == framework) return; throw new LeasingValidationException("LeasingConcurrency"); }
        var f = work.State.Framework ?? throw new UnauthorizedAccessException(); CheckRevision(f.Revision, revision, true); Reason(reason, true);
        if (request == Guid.Empty || limit < 0 || limit > 1000000000000 || !Precision(limit, 2) || document?.Length > 500) throw new LeasingValidationException("LeasingInvalidFramework");
        await work.SaveLimitAsync(new() { Id = request, AccountId = account, FrameworkId = framework, PreviousLimit = f.Limit, NewLimit = limit, Reason = reason, DocumentReference = document, ProposedByUserId = actor.UserId, ProposedUtc = clock.GetUtcNow() }, null, ct); await work.CommitAsync(ct);
    }
    public async Task ApproveLimitAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct); if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveLimit)) throw new UnauthorizedAccessException();
        await using var work = await data.BeginAsync(account, null, null, id, null, ct); actor = await Revalidate(actor, ct);
        if (!LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveLimit)) throw new UnauthorizedAccessException();
        var c = work.State.Change ?? throw new UnauthorizedAccessException(); if (c.ApprovedUtc.HasValue) return;
        var f = work.State.Framework ?? throw new UnauthorizedAccessException(); if (f.Limit != c.PreviousLimit) throw new LeasingValidationException("LeasingConcurrency");
        var before = Snapshot(f); f.Limit = c.NewLimit; f.Revision = Guid.NewGuid(); await Capacity(work, ct);
        c.ApprovedByUserId = actor.UserId; c.ApprovedUtc = clock.GetUtcNow();
        await work.SaveLimitAsync(c, new() { Id = Guid.NewGuid(), AccountId = account, FrameworkId = f.Id, ActorUserId = actor.UserId, RecordedUtc = clock.GetUtcNow(), Action = "LimitApproved", Reason = c.Reason, BeforeJson = before, AfterJson = Snapshot(f) }, ct); await work.CommitAsync(ct);
    }
    private static async Task Capacity(ILeasingOrderWork work, CancellationToken ct) { if ((await work.ReadCapacityAsync(ct)).Available < 0) throw new LeasingValidationException("LeasingLimitExceeded"); }
    private LeasingOrderEvent Event(LeasingOrder o, Guid user, string action, string reason, string before, Guid request) => new() { Id = request, AccountId = o.AccountId, OrderId = o.Id, ActorUserId = user, Action = action, Reason = reason, BeforeJson = before, AfterJson = Snapshot(o), RecordedUtc = clock.GetUtcNow() };
    private static void CheckRevision(Guid actual, Guid supplied, bool exists) { if (exists && actual != supplied) throw new LeasingValidationException("LeasingConcurrency"); }
    private static void Reason(string reason, bool required) { if (required && string.IsNullOrWhiteSpace(reason) || reason?.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired"); }
    private static bool Precision(decimal value, int places) => decimal.Round(value, places) == value;
    private static string Snapshot<T>(T entity) => JsonSerializer.Serialize(entity);
}
