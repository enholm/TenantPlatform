using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Leasing;

public sealed record LeasingCapacity(decimal Limit, decimal Used, decimal Reserved)
{
    public decimal Available => Limit - Used - Reserved;
}
public sealed class OrderDelivery
{
    public Guid OrderId { get; set; }
    public Guid Revision { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public bool VarianceConfirmed { get; set; }
    public List<OrderDeliveryLine> Lines { get; set; } = [];
}
public sealed class OrderDeliveryLine
{
    public Guid OrderLineId { get; set; }
    public int ItemPosition { get; set; }
    public decimal Scope { get; set; }
}
public sealed record OrderDetails(LeasingOrder Order, LeasingFramework Framework, LeasingCapacity Capacity,
    List<LeasingOrderRealization> Realizations, List<LeasingOrderEvent> History, List<LeasingInvoice> Invoices,
    List<LeasingDocument> Documents, bool CanApprove, bool NeedsReview, decimal DocumentedNet = 0, bool CanEdit = false);

public sealed partial class LeasingService
{
    public async Task<bool> CanChangeLimitAsync(Guid account, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); await Member(db, account, ct); return await authorization.CanChangeLeasingLimitAsync(ct); }
    private static IQueryable<LeasingOrder> Orders(TenantPlatformDbContext db, Guid account, Guid user, bool admin) =>
        db.LeasingOrders.Where(x => x.AccountId == account && (admin || x.OwnerUserId == user ||
            db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)));
    private static IQueryable<LeasingAcquisition> ReadAcquisitions(TenantPlatformDbContext db, Guid account, Guid user, bool admin, bool orderApprover) =>
        db.LeasingAcquisitions.Where(x => x.AccountId == account && (admin || x.OwnerUserId == user ||
            db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user) ||
            orderApprover && db.LeasingOrderRealizations.Any(r => r.AccountId == account && r.AcquisitionId == x.Id)));
    private async Task<bool> OrderReader(bool admin, CancellationToken ct) => admin || await authorization.CanApproveLeasingOrdersAsync(ct);
    private static async Task<decimal> Reserved(TenantPlatformDbContext db, Guid account, Guid framework, bool vat, CancellationToken ct)
    {
        var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework && x.Status == LeasingOrderStatus.Approved).Include(x => x.Lines).ToListAsync(ct);
        return orders.Sum(x => LeasingOrderCalculator.Reserved(x, vat));
    }
    private static async Task<LeasingCapacity> Capacity(TenantPlatformDbContext db, LeasingFramework f, CancellationToken ct) => new(f.Limit,
        await db.LeasingAcquisitions.Where(x => x.AccountId == f.AccountId && x.FrameworkId == f.Id && x.Status == LeasingAcquisitionStatus.Registered)
            .SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct),
        await Reserved(db, f.AccountId, f.Id, f.IncludesVat, ct));
    private static async Task CheckCapacity(TenantPlatformDbContext db, LeasingFramework f, CancellationToken ct)
    { if ((await Capacity(db, f, ct)).Available < 0) throw new LeasingValidationException("LeasingLimitExceeded"); }
    private async Task<DateOnly> OrderToday(TenantPlatformDbContext db, Guid account, CancellationToken ct) =>
        AgreementReminderSchedule.Today(clock.GetUtcNow(), await db.AgreementReminderSettings.Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo");
    private void OrderEvent(TenantPlatformDbContext db, LeasingOrder o, Guid user, string action, string reason, string before, Guid request) =>
        db.LeasingOrderEvents.Add(new() { Id = request, AccountId = o.AccountId, OrderId = o.Id, ActorUserId = user,
            Action = action, Reason = reason, BeforeJson = before, AfterJson = Snapshot(o), RecordedUtc = clock.GetUtcNow() });
    private static async Task<bool> OrderRetry(TenantPlatformDbContext db, Guid account, Guid order, Guid request, Guid user, string action, CancellationToken ct)
    {
        if (request == Guid.Empty) throw new LeasingValidationException("LeasingOrderInvalid");
        var previous = await db.LeasingOrderEvents.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct);
        if (previous == null) return false;
        if (previous.OrderId != order || previous.ActorUserId != user || previous.Action != action) throw new LeasingValidationException("LeasingConcurrency");
        return true;
    }
    public async Task<List<LeasingOrder>> ListOrdersAsync(Guid account, Guid? framework = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        return await Orders(db, account, user, await OrderReader(admin, ct)).AsNoTracking().Include(x => x.Lines)
            .Where(x => !framework.HasValue || x.FrameworkId == framework).OrderByDescending(x => x.OrderDate).ThenBy(x => x.Number).ToListAsync(ct);
    }
    public async Task<OrderDetails> GetOrderAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var o = await Orders(db, account, user, await OrderReader(admin, ct)).AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        var f = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct);
        var links = await db.LeasingOrderRealizations.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).ToListAsync(ct);
        var aids = links.Select(x => x.AcquisitionId).ToArray();
        var itemIds = links.Select(x => x.ItemId).ToArray();
        var documented = await (from line in db.LeasingInvoiceLines.AsNoTracking()
            join invoice in db.LeasingInvoices.AsNoTracking() on new { line.AccountId, Id = line.InvoiceId } equals new { invoice.AccountId, invoice.Id }
            where line.AccountId == account && itemIds.Contains(line.ItemId) && invoice.Status == LeasingInvoiceStatus.Approved
            select invoice.Kind == LeasingInvoiceKind.CreditNote ? -line.Net : line.Net).SumAsync(ct);
        return new(o, f, await Capacity(db, f, ct), links,
            await db.LeasingOrderEvents.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct),
            await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && aids.Contains(x.AcquisitionId ?? Guid.Empty)).ToListAsync(ct),
            await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).ToListAsync(ct),
            await authorization.CanApproveLeasingOrdersAsync(ct), o.Status == LeasingOrderStatus.Approved && LeasingOrderCalculator.Reserved(o, f.IncludesVat) > 0 &&
                (f.Status != LeasingFrameworkStatus.Open || await OrderToday(db, account, ct) > f.AcquisitionTo), documented, await Orders(db, account, user, admin).AnyAsync(x => x.Id == id, ct));
    }
    private async Task ValidateOrder(TenantPlatformDbContext db, Guid account, LeasingOrder input, LeasingOrder? original, CancellationToken ct)
    {
        await References(db, account, input.SupplierOrganizationId, input.OwnerUserId, ct);
        if (input.OrderDate == default || input.ExpectedDate == DateOnly.MinValue || input.Notes?.Length > 10000 || input.Lines.Count is < 1 or > 500) throw new LeasingValidationException("LeasingOrderInvalid");
        if (original?.Lines.Any(x => x.Fulfilled + x.Unreserved > 0) == true && original.SupplierOrganizationId != input.SupplierOrganizationId) throw new LeasingValidationException("LeasingOrderInvalid");
        var catalog = await Catalog(db, account, false, ct);
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
            await using var scratch = await factory.CreateDbContextAsync(ct);
            var item = line.Item(); item.Id = Guid.NewGuid(); item.AccountId = account;
            ApplyClassification(scratch, item, classification.Copy(), catalog, false);
            line.ClassificationJson = Snapshot(classification.Copy());
        }
        if (input.Lines.Sum(x => LeasingCalculator.Line(x.Item()).Gross) > 1000000000000m || original?.Lines.Any(x => x.Fulfilled + x.Unreserved > 0 && !ids.Contains(x.Id)) == true) throw new LeasingValidationException("LeasingOrderInvalid");
    }
    private static void CopyOrder(LeasingOrder target, LeasingOrder input)
    {
        target.SupplierOrganizationId = input.SupplierOrganizationId; target.OwnerUserId = input.OwnerUserId;
        target.OrderDate = input.OrderDate; target.ExpectedDate = input.ExpectedDate; target.Notes = input.Notes;
    }
    private static void CopyLines(TenantPlatformDbContext db, LeasingOrder target, LeasingOrder input)
    {
        foreach (var old in target.Lines.Where(x => input.Lines.All(n => n.Id != x.Id)).ToList()) { db.LeasingOrderLines.Remove(old); target.Lines.Remove(old); }
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var source = input.Lines[i]; var line = target.Lines.SingleOrDefault(x => x.Id == source.Id);
            if (line == null) { line = new() { Id = source.Id, AccountId = target.AccountId, OrderId = target.Id }; target.Lines.Add(line); db.LeasingOrderLines.Add(line); }
            line.Position = i; line.Description = source.Description; line.ItemNumber = source.ItemNumber; line.Unit = source.Unit;
            line.Quantity = source.Quantity; line.UnitPrice = source.UnitPrice; line.VatPercent = source.VatPercent; line.Method = source.Method; line.ClassificationJson = source.ClassificationJson;
        }
    }
    public async Task<Guid> SaveOrderAsync(Guid account, Guid? id, LeasingOrder input, string reason, Guid request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (input.FrameworkId == Guid.Empty) throw new LeasingValidationException("OrderFrameworkRequired");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var oid = id ?? request;
        if (await OrderRetry(db, account, oid, request, user, "Saved", ct)) return oid;
        var o = id.HasValue ? await Orders(db, account, user, admin).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException()
            : new LeasingOrder { Id = oid, AccountId = account, FrameworkId = input.FrameworkId, Status = LeasingOrderStatus.Draft };
        var f = await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == input.FrameworkId && (admin || x.OwnerUserId == user || (id.HasValue && o.FrameworkId == x.Id && o.OwnerUserId == user)), ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(o.Revision, input.Revision, id.HasValue); Reason(reason, id.HasValue);
        if (id.HasValue && o.FrameworkId != input.FrameworkId || input.Currency != f.Currency || o.Status is LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed or LeasingOrderStatus.Pending) throw new LeasingValidationException("LeasingInvalidTransition");
        if (!admin && id.HasValue && o.OwnerUserId != input.OwnerUserId) throw new UnauthorizedAccessException();
        if (o.Lines.Any(x => x.Fulfilled + x.Unreserved > 0) && o.SupplierOrganizationId != input.SupplierOrganizationId) throw new LeasingValidationException("LeasingOrderInvalid");
        await ValidateOrder(db, account, input, o, ct); var before = Snapshot(o);
        o.ProposedByUserId = user;
        if (o.Status == LeasingOrderStatus.Approved) o.ProposalJson = Snapshot(input);
        else { CopyOrder(o, input); CopyLines(db, o, input); o.Currency = f.Currency; o.Status = LeasingOrderStatus.Draft; }
        o.Revision = Guid.NewGuid();
        if (!id.HasValue) { o.Number = $"B-{await db.LeasingOrders.CountAsync(x => x.AccountId == account, ct) + 1:000000}"; db.LeasingOrders.Add(o); }
        OrderEvent(db, o, user, "Saved", reason, before, request);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return o.Id;
    }
    public async Task OrderActionAsync(Guid account, Guid id, Guid revision, LeasingOrderStatus status, string reason, Guid request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var o = await Orders(db, account, user, await OrderReader(admin, ct)).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (await OrderRetry(db, account, id, request, user, status.ToString(), ct)) return;
        CheckRevision(o.Revision, revision, true); Reason(reason, true);
        var before = Snapshot(o); var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct);
        if (status is LeasingOrderStatus.Approved or LeasingOrderStatus.Rejected)
        {
            if (!await authorization.CanApproveLeasingOrdersAsync(ct)) throw new UnauthorizedAccessException();
            if (o.Status != LeasingOrderStatus.Pending && !(o.Status == LeasingOrderStatus.Approved && o.ProposalJson != null)) throw new LeasingValidationException("LeasingInvalidTransition");
            if (status == LeasingOrderStatus.Approved)
            {
                if (f.Status != LeasingFrameworkStatus.Open || !LeasingCalculator.InPeriod(await OrderToday(db, account, ct), f.AcquisitionFrom, f.AcquisitionTo)) throw new LeasingValidationException("LeasingOutsidePeriod");
                if (o.ProposalJson != null) { var proposal = JsonSerializer.Deserialize<LeasingOrder>(o.ProposalJson)!; await ValidateOrder(db, account, proposal, o, ct); CopyOrder(o, proposal); CopyLines(db, o, proposal); }
                o.Status = status; o.ApprovedByUserId = user; o.ApprovedUtc = clock.GetUtcNow();
            }
            else if (o.ProposalJson == null) o.Status = status;
            o.ProposalJson = null;
        }
        else
        {
            if (!await Orders(db, account, user, admin).AnyAsync(x => x.Id == id, ct)) throw new UnauthorizedAccessException();
            var valid = status == LeasingOrderStatus.Pending && o.Status is LeasingOrderStatus.Draft or LeasingOrderStatus.Rejected ||
                status is LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed && o.Status is not (LeasingOrderStatus.Cancelled or LeasingOrderStatus.Closed);
            if (!valid) throw new LeasingValidationException("LeasingInvalidTransition");
            o.Status = status; o.ProposalJson = null;
        }
        o.Revision = Guid.NewGuid(); OrderEvent(db, o, user, status.ToString(), reason, before, request);
        await db.SaveChangesAsync(ct); await CheckCapacity(db, f, ct); await tx.CommitAsync(ct);
    }
    private async Task<LeasingOrder> ConsumeOrder(TenantPlatformDbContext db, Guid account, Guid user, bool admin, LeasingAcquisition input, OrderDelivery delivery, CancellationToken ct)
    {
        var o = await Orders(db, account, user, admin).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == delivery.OrderId, ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(o.Revision, delivery.Revision, true);
        if (o.Status != LeasingOrderStatus.Approved || o.FrameworkId != input.FrameworkId || o.Currency != input.Currency || o.SupplierOrganizationId != input.SupplierOrganizationId || input.Status != LeasingAcquisitionStatus.Registered ||
            delivery.Lines.Count == 0 || delivery.Lines.Select(x => x.ItemPosition).Distinct().Count() != delivery.Lines.Count || delivery.Lines.Any(x => x.ItemPosition < 0 || x.ItemPosition >= input.Items.Count)) throw new LeasingValidationException("LeasingOrderInvalid");
        if (await db.LeasingOrderRealizations.AnyAsync(x => x.AccountId == account && x.AcquisitionId == input.Id && x.OrderId != o.Id, ct)) throw new LeasingValidationException("LeasingOrderOnePurchase");
        var before = Snapshot(o);
        foreach (var part in delivery.Lines)
        {
            var line = o.Lines.SingleOrDefault(x => x.Id == part.OrderLineId) ?? throw new LeasingValidationException("LeasingOrderInvalid");
            if (part.Scope <= 0 || !Precision(part.Scope, 4) || part.Scope + line.Fulfilled + line.Unreserved > line.Scope) throw new LeasingValidationException("LeasingOrderOverDelivery");
            var item = input.Items.Single(x => x.Position == part.ItemPosition);
            if (await db.LeasingOrderRealizations.AnyAsync(x => x.AccountId == account && x.ItemId == item.Id, ct)) throw new LeasingValidationException("LeasingOrderOverDelivery");
            var remaining = LeasingOrderCalculator.Remaining(line); line.Fulfilled += part.Scope;
            var after = LeasingOrderCalculator.Remaining(line); var net = remaining.Net - after.Net; var vat = remaining.Vat - after.Vat;
            var actual = LeasingCalculator.Line(item);
            if (actual.Net <= 0) throw new LeasingValidationException("LeasingOrderInvalid");
            if (line.Method == LeasingOrderMethod.Quantity && part.Scope != item.Quantity) throw new LeasingValidationException("LeasingOrderOverDelivery");
            if (actual.Net != net || actual.Vat != vat)
                if (!delivery.VarianceConfirmed) throw new LeasingValidationException("LeasingOrderConfirmVariance");
            db.LeasingOrderRealizations.Add(new() { Id = Guid.NewGuid(), AccountId = account, OrderId = o.Id, OrderLineId = line.Id,
                AcquisitionId = input.Id, ItemId = item.Id, RequestId = delivery.RequestId, Scope = part.Scope, ApprovedNet = net, ApprovedVat = vat,
                ActualNet = actual.Net, ActualVat = actual.Vat, ActorUserId = user, RecordedUtc = clock.GetUtcNow() });
        }
        o.Revision = Guid.NewGuid(); OrderEvent(db, o, user, "Realized", delivery.VarianceConfirmed ? "Variance confirmed" : "", before, delivery.RequestId);
        return o;
    }
    public async Task LinkOrderAcquisitionAsync(Guid account, Guid acquisition, Guid revision, OrderDelivery delivery, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        if (await OrderRetry(db, account, delivery.OrderId, delivery.RequestId, user, "Realized", ct)) return;
        var a = await WithClassifications(Acquisitions(db, account, user, admin)).SingleOrDefaultAsync(x => x.Id == acquisition, ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(a.Revision, revision, true);
        var reversed = await db.LeasingInvoiceLines.Where(x => x.AccountId == account && x.CreatedItem && db.LeasingInvoices.Any(d => d.AccountId == account && d.Id == x.InvoiceId && d.Status == LeasingInvoiceStatus.Reversed)).Select(x => x.ItemId).ToListAsync(ct);
        if (delivery.Lines.Any(x => x.ItemPosition >= 0 && x.ItemPosition < a.Items.Count && reversed.Contains(a.Items.Single(i => i.Position == x.ItemPosition).Id))) throw new LeasingValidationException("LeasingOrderInvalid");
        await ConsumeOrder(db, account, user, admin, a, delivery, ct); a.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    // Suggestions only: required dimensions are validated by SaveAcquisitionCore on registration.
    public static LeasingClassificationInput DeliveryClassification(LeasingOrderLine line, LeasingItem item)
    {
        var value = (JsonSerializer.Deserialize<LeasingClassificationInput>(line.ClassificationJson) ?? new()).Copy();
        if (value.Mode is LeasingAllocationMode.Quantity or LeasingAllocationMode.NetAmount && value.Rows.Count > 0)
        {
            var total = value.Rows.Sum(x => x.InputValue);
            var target = value.Mode == LeasingAllocationMode.Quantity ? item.Quantity : LeasingCalculator.Line(item).Net;
            if (total > 0)
            {
                decimal allocated = 0;
                for (int i = 0; i < value.Rows.Count; i++) { var row = value.Rows[i]; row.InputValue = i == value.Rows.Count - 1 ? target - allocated : decimal.Round(target * row.InputValue / total, value.Mode == LeasingAllocationMode.Quantity ? 4 : 2, MidpointRounding.AwayFromZero); allocated += row.InputValue; }
            }
        }
        return value;
    }
    private async Task ReverseOrderParts(TenantPlatformDbContext db, LeasingAcquisition a, Guid user, IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.ToArray();
        var links = await db.LeasingOrderRealizations.Where(x => x.AccountId == a.AccountId && x.AcquisitionId == a.Id && ids.Contains(x.ItemId) && !x.Reversed).ToListAsync(ct);
        foreach (var group in links.GroupBy(x => x.OrderId))
        {
            var o = await db.LeasingOrders.Include(x => x.Lines).SingleAsync(x => x.AccountId == a.AccountId && x.Id == group.Key, ct); var before = Snapshot(o);
            foreach (var link in group) { var line = o.Lines.Single(x => x.Id == link.OrderLineId); line.Fulfilled -= link.Scope; line.Unreserved += link.Scope; link.Reversed = true; }
            o.Revision = Guid.NewGuid(); OrderEvent(db, o, user, "Reversed", "No reservation reopened", before, Guid.NewGuid());
        }
    }
    public async Task ReopenOrderAsync(Guid account, Guid id, Guid revision, string reason, Guid request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await authorization.CanApproveLeasingOrdersAsync(ct)) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var o = await Orders(db, account, user, true).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (await OrderRetry(db, account, id, request, user, "Reopened", ct)) return;
        CheckRevision(o.Revision, revision, true); Reason(reason, true);
        if (o.Status != LeasingOrderStatus.Approved || o.Lines.All(x => x.Unreserved == 0)) throw new LeasingValidationException("LeasingInvalidTransition");
        var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct);
        if (f.Status != LeasingFrameworkStatus.Open || !LeasingCalculator.InPeriod(await OrderToday(db, account, ct), f.AcquisitionFrom, f.AcquisitionTo)) throw new LeasingValidationException("LeasingOutsidePeriod");
        var before = Snapshot(o); foreach (var line in o.Lines) line.Unreserved = 0;
        o.Revision = Guid.NewGuid(); OrderEvent(db, o, user, "Reopened", reason, before, request);
        await db.SaveChangesAsync(ct); await CheckCapacity(db, f, ct); await tx.CommitAsync(ct);
    }
    public async Task<List<LeasingLimitChange>> LimitChangesAsync(Guid account, Guid framework, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await Frameworks(db, account, user, admin || await authorization.CanChangeLeasingLimitAsync(ct)).AnyAsync(x => x.Id == framework, ct)) throw new UnauthorizedAccessException();
        return await db.LeasingLimitChanges.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework).OrderByDescending(x => x.ProposedUtc).ToListAsync(ct);
    }
    public async Task ProposeLimitAsync(Guid account, Guid framework, Guid revision, decimal limit, string reason, string? document, Guid request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await authorization.CanChangeLeasingLimitAsync(ct)) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        if (await db.LeasingLimitChanges.AnyAsync(x => x.AccountId == account && x.Id == request && x.ProposedByUserId == user && x.FrameworkId == framework, ct)) return;
        var f = await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == framework, ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(f.Revision, revision, true); Reason(reason, true);
        if (request == Guid.Empty || limit < 0 || limit > 1000000000000 || !Precision(limit, 2) || document?.Length > 500) throw new LeasingValidationException("LeasingInvalidFramework");
        db.LeasingLimitChanges.Add(new() { Id = request, AccountId = account, FrameworkId = framework, PreviousLimit = f.Limit, NewLimit = limit,
            Reason = reason, DocumentReference = document, ProposedByUserId = user, ProposedUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ApproveLimitAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await authorization.CanChangeLeasingLimitAsync(ct)) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var change = await db.LeasingLimitChanges.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (change.ApprovedUtc.HasValue) return;
        var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == account && x.Id == change.FrameworkId, ct);
        if (f.Limit != change.PreviousLimit) throw new LeasingValidationException("LeasingConcurrency");
        var before = Snapshot(f); f.Limit = change.NewLimit; f.Revision = Guid.NewGuid(); await CheckCapacity(db, f, ct);
        change.ApprovedByUserId = user; change.ApprovedUtc = clock.GetUtcNow();
        History(db, account, f.Id, null, user, "LimitApproved", change.Reason, before, Snapshot(f));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
