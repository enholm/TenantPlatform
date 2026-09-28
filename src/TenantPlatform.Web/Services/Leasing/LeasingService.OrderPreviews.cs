using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public async Task<LeasingAcquisition> NewOrderAcquisitionAsync(Guid account, Guid orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var o = await Orders(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new UnauthorizedAccessException();
        if (o.Status != LeasingOrderStatus.Approved) throw new LeasingValidationException("LeasingInvalidTransition");
        var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct);
        return new() { FrameworkId = f.Id, FinanceOrganizationId = f.FinanceOrganizationId, SupplierOrganizationId = o.SupplierOrganizationId,
            OwnerUserId = user, Currency = f.Currency, Terms = f.Terms.Copy(), PurchaseDate = await OrderToday(db, account, ct), Name = o.Number, Reference = o.Number };
    }
    public async Task<List<LeasingOrder>> AcquisitionOrdersAsync(Guid account, Guid acquisition, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await ReadAcquisitions(db, account, user, admin, await authorization.CanApproveLeasingOrdersAsync(ct)).AnyAsync(x => x.Id == acquisition, ct)) throw new UnauthorizedAccessException();
        return await Orders(db, account, user, await OrderReader(admin, ct)).AsNoTracking().Include(x => x.Lines)
            .Where(x => db.LeasingOrderRealizations.Any(r => r.AccountId == account && r.OrderId == x.Id && r.AcquisitionId == acquisition)).ToListAsync(ct);
    }
    public static LeasingItem OrderClassificationPreview(LeasingOrderLine line, LeasingClassificationCatalog catalog)
    {
        var input = JsonSerializer.Deserialize<LeasingClassificationInput>(line.ClassificationJson) ?? new(); var item = line.Item(); item.AllocationMode = input.Mode;
        item.AllocationDimensions = input.VaryingDimensions.Select(id => new LeasingAllocationDimension { DimensionId = id, DimensionName = catalog.Dimensions.FirstOrDefault(x => x.Dimension.Id == id)?.Dimension.Name ?? "" }).ToList();
        void Choices(IEnumerable<LeasingDimensionChoice> choices, Guid? row)
        {
            foreach (var choice in choices)
            {
                var dimension = catalog.Dimensions.FirstOrDefault(x => x.Dimension.Id == choice.DimensionId); var value = dimension?.Values.FirstOrDefault(x => x.Id == choice.ValueId);
                item.DimensionSelections.Add(new() { DimensionId = choice.DimensionId, ValueId = choice.ValueId, AllocationRowId = row, DimensionName = dimension?.Dimension.Name ?? "", ValueName = value?.Name ?? "", ValuePath = value?.Path ?? "" });
            }
        }
        Choices(input.Common, null);
        for (int i = 0; i < input.Rows.Count; i++) { var row = input.Rows[i]; var id = Guid.NewGuid(); item.AllocationRows.Add(new() { Id = id, Position = i, InputValue = row.InputValue }); Choices(row.Choices, id); }
        return item;
    }
    public static string FulfillmentKey(LeasingOrder o) => o.Lines.All(x => x.Fulfilled == 0) ? "OrderNotRealized" : o.Lines.All(x => x.Fulfilled == x.Scope) ? "OrderFullyRealized" : "OrderPartRealized";
    public static decimal ApprovedOrderValue(OrderDetails d)
    {
        if (d.Order.ApprovedUtc == null) return 0;
        var pending = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(d.Order))!;
        // Reversed history is retained, but must not count twice after an explicit reopening.
        // Unreserved scope is still part of the approved order value, not active capacity usage.
        foreach (var line in pending.Lines) line.Unreserved = 0;
        return d.Realizations.Where(x => !x.Reversed).Sum(x => x.ApprovedNet + (d.Framework.IncludesVat ? x.ApprovedVat : 0)) +
            pending.Lines.Sum(x => d.Framework.IncludesVat ? LeasingOrderCalculator.Remaining(x).Gross : LeasingOrderCalculator.Remaining(x).Net);
    }
    public static decimal ProposedReservation(LeasingOrder current, LeasingOrder proposed, bool vat)
    {
        var copy = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(proposed))!; copy.Status = LeasingOrderStatus.Approved;
        foreach (var line in copy.Lines) { var old = current.Lines.SingleOrDefault(x => x.Id == line.Id); line.Fulfilled = old?.Fulfilled ?? 0; line.Unreserved = old?.Unreserved ?? 0; }
        return LeasingOrderCalculator.Reserved(copy, vat);
    }
    public static decimal PreviewOrderRelease(LeasingOrder order, OrderDelivery delivery, bool vat)
    {
        var copy = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(order))!;
        var before = LeasingOrderCalculator.Reserved(copy, vat);
        foreach (var part in delivery.Lines.Where(x => x.OrderLineId != Guid.Empty))
        {
            var line = copy.Lines.Single(x => x.Id == part.OrderLineId);
            if (part.Scope <= 0 || part.Scope + line.Fulfilled + line.Unreserved > line.Scope) throw new LeasingValidationException("LeasingOrderOverDelivery");
            line.Fulfilled += part.Scope;
        }
        return before - LeasingOrderCalculator.Reserved(copy, vat);
    }
    public async Task<List<LeasingAcquisition>> OrderLinkCandidatesAsync(Guid account, Guid order, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var o = await Orders(db, account, user, await OrderReader(admin, ct)).SingleOrDefaultAsync(x => x.Id == order, ct) ?? throw new UnauthorizedAccessException();
        return await Acquisitions(db, account, user, admin).AsNoTracking().Where(x => x.FrameworkId == o.FrameworkId && x.Currency == o.Currency && x.SupplierOrganizationId == o.SupplierOrganizationId && x.Status == LeasingAcquisitionStatus.Registered &&
            !db.LeasingOrderRealizations.Any(r => r.AccountId == account && r.AcquisitionId == x.Id && r.OrderId != o.Id)).OrderByDescending(x => x.PurchaseDate).ToListAsync(ct);
    }
}
