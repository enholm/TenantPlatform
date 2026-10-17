using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;

public sealed class LeasingAcquisitionWriter(IAuthorizationService authorization, ILeasingAcquisitionData data, TimeProvider clock)
{
    private async Task<LeasingUserScope> Actor(Guid account, CancellationToken ct) => LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
    private async Task<LeasingUserScope> Revalidate(LeasingUserScope before, CancellationToken ct)
    {
        var current = await Actor(before.AccountId, ct); if (current.UserId != before.UserId) throw new UnauthorizedAccessException(); return current;
    }
    private static bool Owns(LeasingUserScope actor, AcquisitionFacts facts) => facts.Acquisition is not null && (actor.IsAdministrator || facts.Acquisition.OwnerUserId == actor.UserId || facts.OriginalFramework?.OwnerUserId == actor.UserId);
    private static AcquisitionLoad Request(Guid? id, LeasingAcquisition input, OrderDelivery? delivery, Guid? invoice = null) => new(id, input.FrameworkId, input.FinanceOrganizationId, input.OwnerUserId, input.SupplierOrganizationId, delivery?.OrderId, delivery?.RequestId, invoice);
    private static T Copy<T>(T input) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input))!;
    public async Task<Guid> SaveAcquisitionAsync(Guid account, Guid? id, LeasingAcquisition input, string reason, CancellationToken ct = default, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications = null, OrderDelivery? delivery = null)
    {
        input = Copy(input); delivery = delivery is null ? null : Copy(delivery); classifications = classifications is null ? null : Copy(classifications.ToDictionary(x => x.Key, x => x.Value));
        var actor = await Actor(account, ct);
        await using var work = await data.BeginAsync(account, ct); actor = await Revalidate(actor, ct);
        var facts = await work.LoadAsync(Request(id, input, delivery), ct);
        if (id.HasValue && !Owns(actor, facts)) throw new UnauthorizedAccessException();
        if (delivery is not null && Retry(facts, delivery, actor)) return facts.Realizations.FirstOrDefault(x => x.OrderId == delivery.OrderId && x.RequestId == delivery.RequestId)?.AcquisitionId ?? throw new LeasingValidationException("LeasingConcurrency");
        var result = await SaveCore(work, facts, actor, id, input, reason, classifications, false, delivery, ct);
        await work.CommitAsync(ct); return result;
    }
    // Used by the enclosing invoice use case within its account-locked transaction.
    // No caller identity/admin flag is accepted, and persisted invoice/resource access is checked here.
    public async Task<Guid> SaveForInvoiceAsync(ILeasingAcquisitionWork work, Guid invoiceId, Guid? id, LeasingAcquisition input, string reason, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications, OrderDelivery? delivery, CancellationToken ct)
    {
        var actor = await Actor(work.AccountId, ct);
        var facts = await work.LoadAsync(Request(id, input, delivery, invoiceId), ct);
        actor = await Revalidate(actor, ct);
        var invoice = facts.ApprovalInvoice ?? throw new UnauthorizedAccessException();
        if (invoice.Category != LeasingInvoiceCategory.Equipment || invoice.Status != LeasingInvoiceStatus.Review || invoice.AcquisitionId != id ||
            !actor.IsAdministrator && (id.HasValue ? !Owns(actor, facts) : invoice.UploadedByUserId != actor.UserId)) throw new UnauthorizedAccessException();
        return await SaveCore(work, facts, actor, id, input, reason, classifications, true, delivery, ct);
    }
    private async Task<Guid> SaveCore(ILeasingAcquisitionWork work, AcquisitionFacts facts, LeasingUserScope actor, Guid? id, LeasingAcquisition input, string reason, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications, bool invoiceApproval, OrderDelivery? delivery, CancellationToken ct)
    {
        var account = actor.AccountId; var user = actor.UserId; var admin = actor.IsAdministrator;
        var entity = id.HasValue ? facts.Acquisition ?? throw new UnauthorizedAccessException() : new LeasingAcquisition { Id = Guid.NewGuid(), AccountId = account };
        if (id.HasValue && !Owns(actor, facts)) throw new UnauthorizedAccessException();
        CheckRevision(entity.Revision, input.Revision, id.HasValue); Reason(reason, id.HasValue);
        if (entity.Status == LeasingAcquisitionStatus.Cancelled || input.Status == LeasingAcquisitionStatus.Cancelled ||
            !Enum.IsDefined(input.Status) || (entity.Status == LeasingAcquisitionStatus.Registered && input.Status != LeasingAcquisitionStatus.Registered))
            throw new LeasingValidationException("LeasingInvalidTransition");
        if (!admin && id.HasValue && entity.OwnerUserId != input.OwnerUserId) throw new UnauthorizedAccessException();
        LeasingFramework? framework = null;
        if (input.FrameworkId.HasValue)
        {
            framework = facts.TargetFramework
                ?? throw new UnauthorizedAccessException();
            // A caller cannot attach a purchase to an inaccessible framework, even within the same tenant.
            if (!admin && framework.OwnerUserId != user && (!id.HasValue || entity.FrameworkId != framework.Id) &&
                !(delivery != null && facts.Orders.Any(o => o.Id == delivery.OrderId && o.FrameworkId == framework.Id && o.Status == LeasingOrderStatus.Approved && (o.OwnerUserId == user || framework.OwnerUserId == user)))) throw new UnauthorizedAccessException();
            if (input.Currency != framework.Currency || input.FinanceOrganizationId != framework.FinanceOrganizationId)
                throw new LeasingValidationException("LeasingFrameworkCurrency");
        }
        else if (!id.HasValue && !admin) throw new UnauthorizedAccessException();
        if (!facts.FinanceExists) throw new LeasingValidationException("LeasingInvalidParty");
        if (!facts.OwnerActiveMember) throw new LeasingValidationException("LeasingInvalidOwner");
        if (!facts.SupplierExists) throw new LeasingValidationException("LeasingInvalidParty");
        ValidateCommon(input.Name, input.Reference, input.Currency, input.Notes, input.Terms);
        if (input.InvoiceNumber?.Length > 100 || input.InvoiceDate == DateOnly.MinValue || input.FinancedAmount < 0 ||
            input.FinancedAmount > 1000000000000m || !Precision(input.FinancedAmount, 2)) throw new LeasingValidationException("LeasingInvalidPurchase");
        DateOnly end;
        try { end = LeasingCalculator.EndDate(input.PurchaseDate, input.Terms.Months); }
        catch (ArgumentOutOfRangeException) { throw new LeasingValidationException("LeasingInvalidDates"); }
        if (input.Items.Count > 500 || input.Items.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Description.Length > 500 ||
            x.ItemNumber?.Length > 100 || x.Quantity <= 0 || x.Quantity > 1000000000m || x.UnitPrice < 0 || x.UnitPrice > 1000000000000m ||
            x.VatPercent is < 0 or > 100 || !Precision(x.Quantity, 4) || !Precision(x.UnitPrice, 4) || !Precision(x.VatPercent, 4)))
            throw new LeasingValidationException("LeasingInvalidLines");
        foreach (var line in input.Items)
        {
            var existing = entity.Items.SingleOrDefault(x => x.Id == line.Id);
            if (!invoiceApproval || existing != null)
            { line.InvoiceNetAdjustment = existing?.InvoiceNetAdjustment ?? 0; line.InvoiceVatAdjustment = existing?.InvoiceVatAdjustment ?? 0; }
        }
        if (id.HasValue && facts.Invoices.Any(x => x.Status == LeasingInvoiceStatus.Approved) &&
            (entity.FrameworkId != input.FrameworkId || entity.SupplierOrganizationId != input.SupplierOrganizationId || entity.Currency != input.Currency)) throw new LeasingValidationException("InvoiceLinkedPurchase");
        if (id.HasValue)
        {
            var linked = facts.Realizations.Where(x => x.AcquisitionId == entity.Id).ToList();
            if (linked.Count > 0 && (input.FrameworkId != entity.FrameworkId || input.Currency != entity.Currency || input.SupplierOrganizationId != entity.SupplierOrganizationId ||
                linked.Any(l => { var old = entity.Items.Single(x => x.Id == l.ItemId); var next = input.Items.SingleOrDefault(x => x.Id == l.ItemId); return next == null || next.Quantity != old.Quantity || next.UnitPrice != old.UnitPrice || next.VatPercent != old.VatPercent; }))) throw new LeasingValidationException("LeasingOrderLinkedImmutable");
        }
        if (id.HasValue && facts.HasFinancing &&
            Snapshot(LeasingFinancingSnapshot.From(entity)) != Snapshot(LeasingFinancingSnapshot.From(input))) throw new LeasingValidationException("PaymentUseFinancingRevision");
        if(id.HasValue)
        {
            foreach(var tracked in entity.Items.Where(x => x.CountableEquipment).ToList())
            {
                var updated=input.Items.SingleOrDefault(x=>x.Id==tracked.Id);
                var units=facts.EquipmentCounts.GetValueOrDefault(tracked.Id);
                var processed=facts.UntrackedQuantities.GetValueOrDefault(tracked.Id);
                if((units>0||processed>0)&&(updated==null||updated.Quantity<units+processed||updated.Quantity!=decimal.Truncate(updated.Quantity)))throw new LeasingValidationException("LifeOverQuantity");
            }
        }
        var retainedIds = input.Items.Select(x => x.Id).ToArray();
        if (id.HasValue && facts.InvoiceLines.Any(x => !retainedIds.Contains(x.ItemId) && entity.Items.Any(i => i.Id == x.ItemId))) throw new LeasingValidationException("InvoiceLinkedPurchase");
        if (id.HasValue)
        {
            var reversedItems = facts.InvoiceLines.Where(x => x.CreatedItem && facts.Invoices.Any(d => d.Id == x.InvoiceId && d.Status == LeasingInvoiceStatus.Reversed)).Select(x => x.ItemId).ToHashSet();
            foreach (var item in input.Items.Where(x => reversedItems.Contains(x.Id)))
            {
                var original = entity.Items.Single(x => x.Id == item.Id);
                if (item.Quantity != original.Quantity || item.UnitPrice != original.UnitPrice || item.VatPercent != original.VatPercent) throw new LeasingValidationException("InvoiceLinkedPurchase");
            }
        }
        var totals = LeasingCalculator.Total(input.Items);
        if (totals.Gross > 1000000000000m || totals.Net < entity.CreditNetTotal + entity.ReversedNetTotal || totals.Vat < entity.CreditVatTotal + entity.ReversedVatTotal) throw new LeasingValidationException("LeasingInvalidLines");
        if (input.Status == LeasingAcquisitionStatus.Registered)
        {
            if (input.Items.Count == 0 || totals.Net <= 0 || input.FinancedAmount <= 0)
                throw new LeasingValidationException("LeasingRegistrationRequired");
            if (framework is not null)
            {
                if ((entity.Status != LeasingAcquisitionStatus.Registered || entity.FrameworkId != framework.Id) && framework.Status != LeasingFrameworkStatus.Open)
                    throw new LeasingValidationException("LeasingClosed");
                if (!LeasingCalculator.InPeriod(input.PurchaseDate, framework.AcquisitionFrom, framework.AcquisitionTo)) throw new LeasingValidationException("LeasingOutsidePeriod");
            }
        }
        if (classifications?.Keys.Any(x => x < 0 || x >= input.Items.Count) == true) throw new LeasingValidationException("LeasingInvalidLines");
        var registering = input.Status == LeasingAcquisitionStatus.Registered && entity.Status != LeasingAcquisitionStatus.Registered;
        var catalog = facts.Catalog;
        var oldIds = entity.Items.Select(x => x.Id).ToHashSet();
        var incomingIds = input.Items.Where(x => x.Id != Guid.Empty).Select(x => x.Id).ToList();
        if (incomingIds.Distinct().Count() != incomingIds.Count || incomingIds.Any(x => !oldIds.Contains(x))) throw new LeasingValidationException("LeasingInvalidLines");
        var before = id.HasValue ? Snapshot(entity) : "{}";
        entity.FrameworkId = input.FrameworkId; entity.Name = input.Name.Trim(); entity.Reference = input.Reference.Trim();
        entity.SupplierOrganizationId = input.SupplierOrganizationId; entity.FinanceOrganizationId = input.FinanceOrganizationId;
        entity.OwnerUserId = input.OwnerUserId; entity.PurchaseDate = input.PurchaseDate; entity.EndDate = end;
        entity.Currency = input.Currency; entity.InvoiceNumber = input.InvoiceNumber?.Trim(); entity.InvoiceDate = input.InvoiceDate;
        entity.NetTotal = totals.Net; entity.VatTotal = totals.Vat; entity.GrossTotal = totals.Gross; entity.FinancedAmount = input.FinancedAmount;
        entity.Terms = input.Terms.Copy(); entity.Notes = input.Notes?.Trim(); entity.Status = input.Status; entity.Revision = Guid.NewGuid();
        foreach (var removed in entity.Items.Where(x => !incomingIds.Contains(x.Id)).ToList()) { entity.Items.Remove(removed); }
        for (var position = 0; position < input.Items.Count; position++)
        {
            var incoming = input.Items[position];
            var item = incoming.Id == Guid.Empty ? new LeasingItem { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = entity.Id } : entity.Items.Single(x => x.Id == incoming.Id);
            item.Position = position; item.Description = incoming.Description.Trim(); item.ItemNumber = incoming.ItemNumber?.Trim();
            item.Quantity = incoming.Quantity; item.UnitPrice = incoming.UnitPrice; item.VatPercent = incoming.VatPercent;
            item.InvoiceNetAdjustment = incoming.InvoiceNetAdjustment; item.InvoiceVatAdjustment = incoming.InvoiceVatAdjustment;
            if (incoming.Id == Guid.Empty) { entity.Items.Add(item); }
            if (classifications != null && classifications.TryGetValue(position, out var classification))
                LeasingClassificationRules.Apply(item, classification, catalog, input.Status == LeasingAcquisitionStatus.Registered);
            else if (registering || incoming.Id == Guid.Empty)
                LeasingClassificationRules.Apply(item, LeasingClassificationInput.From(item), catalog, input.Status == LeasingAcquisitionStatus.Registered);
            if (input.Status == LeasingAcquisitionStatus.Registered && !LeasingAllocationCalculator.Calculate(item).Complete)
                throw new LeasingValidationException("LeasingInvalidAllocation");
        }
        await work.SaveAcquisitionAsync(entity, History(account, entity.Id, user, id.HasValue ? "Updated" : "Created", reason, before, Snapshot(entity)), ct);
        await work.SaveFinancingAsync(LeasingInitialFinancing.Create(entity, user, clock.GetUtcNow(), facts.HasFinancing), ct);
        if (delivery != null)
        {
            var change = Consume(facts, entity, delivery, user, admin);
            await work.SaveOrdersAsync([change.Event], change.Links, ct);
        }
        if (framework != null && entity.Status == LeasingAcquisitionStatus.Registered && (await work.ReadCapacityAsync(framework.Id, ct)).Available < 0) throw new LeasingValidationException("LeasingLimitExceeded");
        return entity.Id;
    }

    private sealed record OrderChange(LeasingOrderEvent Event, List<LeasingOrderRealization> Links);
    private OrderChange Consume(AcquisitionFacts facts, LeasingAcquisition input, OrderDelivery delivery, Guid user, bool admin)
    {
        var o = facts.Orders.SingleOrDefault(x => x.Id == delivery.OrderId) ?? throw new UnauthorizedAccessException();
        if (!admin && o.OwnerUserId != user && !(facts.TargetFramework?.Id == o.FrameworkId && facts.TargetFramework.OwnerUserId == user) && !(facts.OriginalFramework?.Id == o.FrameworkId && facts.OriginalFramework.OwnerUserId == user)) throw new UnauthorizedAccessException();
        CheckRevision(o.Revision, delivery.Revision, true);
        if (o.Status != LeasingOrderStatus.Approved || o.FrameworkId != input.FrameworkId || o.Currency != input.Currency || o.SupplierOrganizationId != input.SupplierOrganizationId || input.Status != LeasingAcquisitionStatus.Registered || delivery.Lines.Count == 0 || delivery.Lines.Select(x => x.ItemPosition).Distinct().Count() != delivery.Lines.Count || delivery.Lines.Any(x => x.ItemPosition < 0 || x.ItemPosition >= input.Items.Count)) throw new LeasingValidationException("LeasingOrderInvalid");
        if (facts.Realizations.Any(x => x.AcquisitionId == input.Id && x.OrderId != o.Id)) throw new LeasingValidationException("LeasingOrderOnePurchase");
        var before = Snapshot(o); var links = new List<LeasingOrderRealization>();
        foreach (var part in delivery.Lines)
        {
            var line = o.Lines.SingleOrDefault(x => x.Id == part.OrderLineId) ?? throw new LeasingValidationException("LeasingOrderInvalid");
            if (part.Scope <= 0 || !Precision(part.Scope, 4) || part.Scope + line.Fulfilled + line.Unreserved > line.Scope) throw new LeasingValidationException("LeasingOrderOverDelivery");
            var item = input.Items.Single(x => x.Position == part.ItemPosition);
            if (facts.Realizations.Any(x => x.ItemId == item.Id)) throw new LeasingValidationException("LeasingOrderOverDelivery");
            var remaining = LeasingOrderCalculator.Remaining(line); line.Fulfilled += part.Scope;
            var after = LeasingOrderCalculator.Remaining(line); var net = remaining.Net - after.Net; var vat = remaining.Vat - after.Vat;
            var actual = LeasingCalculator.Line(item);
            if (actual.Net <= 0) throw new LeasingValidationException("LeasingOrderInvalid");
            if (line.Method == LeasingOrderMethod.Quantity && part.Scope != item.Quantity) throw new LeasingValidationException("LeasingOrderOverDelivery");
            if ((actual.Net != net || actual.Vat != vat) && !delivery.VarianceConfirmed) throw new LeasingValidationException("LeasingOrderConfirmVariance");
            links.Add(new() { Id = Guid.NewGuid(), AccountId = input.AccountId, OrderId = o.Id, OrderLineId = line.Id, AcquisitionId = input.Id, ItemId = item.Id, RequestId = delivery.RequestId, Scope = part.Scope, ApprovedNet = net, ApprovedVat = vat, ActualNet = actual.Net, ActualVat = actual.Vat, ActorUserId = user, RecordedUtc = clock.GetUtcNow() });
        }
        o.Revision = Guid.NewGuid(); return new(OrderEvent(o, user, "Realized", delivery.VarianceConfirmed ? "Variance confirmed" : "", before, delivery.RequestId), links);
    }
    private static bool Retry(AcquisitionFacts facts, OrderDelivery delivery, LeasingUserScope actor)
    {
        if (delivery.RequestId == Guid.Empty) throw new LeasingValidationException("LeasingOrderInvalid");
        var old = facts.PreviousOrderRequest; if (old is null) return false;
        var o = facts.Orders.SingleOrDefault(x => x.Id == delivery.OrderId) ?? throw new UnauthorizedAccessException();
        if (!actor.IsAdministrator && o.OwnerUserId != actor.UserId && !(facts.TargetFramework?.Id == o.FrameworkId && facts.TargetFramework.OwnerUserId == actor.UserId) && !(facts.OriginalFramework?.Id == o.FrameworkId && facts.OriginalFramework.OwnerUserId == actor.UserId)) throw new UnauthorizedAccessException();
        if (old.OrderId != delivery.OrderId || old.ActorUserId != actor.UserId || old.Action != "Realized") throw new LeasingValidationException("LeasingConcurrency");
        return true;
    }
    public async Task CancelAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct);
        await using var work = await data.BeginAsync(account, ct); actor = await Revalidate(actor, ct);
        var facts = await work.LoadAsync(new(id, null, Guid.Empty, Guid.Empty, Guid.Empty), ct);
        if (!Owns(actor, facts)) throw new UnauthorizedAccessException();
        var a = facts.Acquisition!; CheckRevision(a.Revision, revision, true); Reason(reason, true);
        if (a.Status == LeasingAcquisitionStatus.Cancelled) throw new LeasingValidationException("LeasingInvalidTransition");
        var events = Reverse(facts, actor.UserId, a.Items.Select(x => x.Id));
        var before = Snapshot(a); a.Status = LeasingAcquisitionStatus.Cancelled; a.Revision = Guid.NewGuid();
        await work.SaveAcquisitionAsync(a, History(account, id, actor.UserId, "Cancelled", reason, before, Snapshot(a)), ct);
        await work.SaveOrdersAsync(events, [], ct); await work.CommitAsync(ct);
    }
    public async Task LinkOrderAcquisitionAsync(Guid account, Guid acquisition, Guid revision, OrderDelivery delivery, CancellationToken ct = default)
    {
        delivery = Copy(delivery); var actor = await Actor(account, ct);
        await using var work = await data.BeginAsync(account, ct); actor = await Revalidate(actor, ct);
        var facts = await work.LoadAsync(new(acquisition, null, Guid.Empty, Guid.Empty, Guid.Empty, delivery.OrderId, delivery.RequestId), ct);
        if (!Owns(actor, facts)) throw new UnauthorizedAccessException();
        if (Retry(facts, delivery, actor)) return;
        var a = facts.Acquisition!; CheckRevision(a.Revision, revision, true);
        var reversed = facts.InvoiceLines.Where(x => x.CreatedItem && facts.Invoices.Any(d => d.Id == x.InvoiceId && d.Status == LeasingInvoiceStatus.Reversed)).Select(x => x.ItemId).ToHashSet();
        if (delivery.Lines.Any(x => x.ItemPosition >= 0 && x.ItemPosition < a.Items.Count && reversed.Contains(a.Items.Single(i => i.Position == x.ItemPosition).Id))) throw new LeasingValidationException("LeasingOrderInvalid");
        var change = Consume(facts, a, delivery, actor.UserId, actor.IsAdministrator); a.Revision = Guid.NewGuid();
        await work.SaveOrdersAsync([change.Event], change.Links, ct); await work.CommitAsync(ct);
    }
    public async Task ReverseOrderPartsAsync(ILeasingAcquisitionWork work, Guid acquisition, IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.ToArray(); var actor = await Actor(work.AccountId, ct);
        var facts = await work.LoadAsync(new(acquisition, null, Guid.Empty, Guid.Empty, Guid.Empty), ct);
        actor = await Revalidate(actor, ct);
        if (!Owns(actor, facts)) throw new UnauthorizedAccessException();
        if (ids.Any(x => facts.Acquisition!.Items.All(i => i.Id != x))) throw new UnauthorizedAccessException();
        await work.SaveOrdersAsync(Reverse(facts, actor.UserId, ids), [], ct);
    }
    private List<LeasingOrderEvent> Reverse(AcquisitionFacts facts, Guid user, IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet(); var events = new List<LeasingOrderEvent>();
        foreach (var group in facts.Realizations.Where(x => selected.Contains(x.ItemId) && !x.Reversed).GroupBy(x => x.OrderId))
        {
            var o = facts.Orders.Single(x => x.Id == group.Key); var before = Snapshot(o);
            foreach (var link in group) { var line = o.Lines.Single(x => x.Id == link.OrderLineId); line.Fulfilled -= link.Scope; line.Unreserved += link.Scope; link.Reversed = true; }
            o.Revision = Guid.NewGuid(); events.Add(OrderEvent(o, user, "Reversed", "No reservation reopened", before, Guid.NewGuid()));
        }
        return events;
    }
    private LeasingOrderEvent OrderEvent(LeasingOrder o, Guid user, string action, string reason, string before, Guid request) => new() { Id = request, AccountId = o.AccountId, OrderId = o.Id, ActorUserId = user, Action = action, Reason = reason, BeforeJson = before, AfterJson = Snapshot(o), RecordedUtc = clock.GetUtcNow() };
    private LeasingHistory History(Guid account, Guid acquisition, Guid user, string action, string reason, string before, string after) => new() { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = acquisition, ActorUserId = user, Action = action, Reason = reason?.Trim() ?? "", BeforeJson = before, AfterJson = after, RecordedUtc = clock.GetUtcNow() };
    private static void CheckRevision(Guid actual, Guid supplied, bool existing) { if (existing && actual != supplied) throw new LeasingValidationException("LeasingConcurrency"); }
    private static bool Precision(decimal value, int places) => decimal.Round(value, places) == value;
    private static void Reason(string? value, bool required) { if (required && string.IsNullOrWhiteSpace(value) || value?.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired"); }
    private static void ValidateCommon(string name, string number, string currency, string? notes, LeasingTerms terms) => LeasingRules.ValidateCommon(name, number, currency, notes, terms);
    private static string Snapshot<T>(T value) => JsonSerializer.Serialize(value);
}
