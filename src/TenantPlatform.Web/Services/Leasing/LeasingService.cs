using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Leasing;

public sealed class LeasingValidationException(string key) : Exception(key);
public sealed record LeasingOption(Guid Id, string Name);
public sealed record LeasingOptions(List<LeasingOption> Parties, List<LeasingOption> Owners, List<LeasingOption> Frameworks, bool CanCreate, bool CanRegisterOrders = false);
public sealed record LeasingRow(Guid Id, bool Framework, string Name, string Number, string Status, string Owner, DateOnly Start, DateOnly End, string Currency, decimal Amount);
public sealed record LeasingPage(List<LeasingRow> Items, int Count, int Page, int PageSize);
public sealed record LeasingHistoryRow(DateTimeOffset RecordedUtc, string Actor, string Action, string Reason, string BeforeJson, string AfterJson);
public sealed record LeasingDetails(LeasingFramework? Framework, LeasingAcquisition? Acquisition, string FinanceName,
    string OwnerName, string? SupplierName, string? FrameworkName, decimal Used, bool CanEdit,
    List<LeasingAcquisition> Acquisitions, List<LeasingDocument> Documents, List<LeasingHistoryRow> History, Dictionary<Guid, string> ReferenceNames, decimal Reserved = 0);

public sealed partial class LeasingService(IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService context, ITenantAuthorizationService authorization, IAgreementDocumentStorage storage,
    TimeProvider clock, ILogger<LeasingService> logger)
{
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;
    private async Task<(Guid User, bool Admin)> Member(TenantPlatformDbContext db, Guid account, CancellationToken ct)
    {
        var c = context.Current;
        if (!c.IsAuthenticated || c.CurrentAccountId != account ||
            !await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == c.UserId && x.User.IsActive, ct))
            throw new UnauthorizedAccessException();
        return (c.UserId, await authorization.CanCreateAgreementAsync(ct));
    }
    private static IQueryable<LeasingFramework> Frameworks(TenantPlatformDbContext db, Guid account, Guid user, bool admin) =>
        db.LeasingFrameworks.Where(x => x.AccountId == account && (admin || x.OwnerUserId == user));
    private static IQueryable<LeasingAcquisition> Acquisitions(TenantPlatformDbContext db, Guid account, Guid user, bool admin) =>
        db.LeasingAcquisitions.Where(x => x.AccountId == account && (admin || x.OwnerUserId == user ||
            db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)));

    public async Task<LeasingOptions> OptionsAsync(Guid account, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        var canReadOrders = await OrderReader(admin, ct) || await authorization.CanChangeLeasingLimitAsync(ct);
        if (!canReadOrders && !await PaymentReader(admin, ct) && !await Orders(db, account, user, false).AnyAsync(ct) && !await Frameworks(db, account, user, false).AnyAsync(ct) &&
            !await Acquisitions(db, account, user, false).AnyAsync(ct)) throw new UnauthorizedAccessException();
        return new(await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name)
                .Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct),
            await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account && x.User.IsActive).OrderBy(x => x.User.FirstName)
                .Select(x => new LeasingOption(x.UserId, x.User.FirstName + " " + x.User.LastName)).ToListAsync(ct),
            await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == account && (canReadOrders || x.OwnerUserId == user || db.LeasingOrders.Any(o => o.AccountId == account && o.FrameworkId == x.Id && o.OwnerUserId == user))).OrderBy(x => x.Name)
                .Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct), admin, admin || await Frameworks(db, account, user, false).AnyAsync(ct));
    }

    public async Task<LeasingPage> ListAsync(Guid account, string? search, string kind, string? status, Guid? owner, int page, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        var frameworkReader = admin || await authorization.CanChangeLeasingLimitAsync(ct);
        var frameworks = Frameworks(db, account, user, frameworkReader).AsNoTracking();
        var acquisitions = Acquisitions(db, account, user, admin).AsNoTracking();
        if (kind == "orders") acquisitions = acquisitions.Where(x => x.FrameworkId == null);
        if (owner.HasValue) { frameworks = frameworks.Where(x => x.OwnerUserId == owner); acquisitions = acquisitions.Where(x => x.OwnerUserId == owner); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            frameworks = frameworks.Where(x => x.Name.ToLower().Contains(term) || x.Number.ToLower().Contains(term));
            acquisitions = acquisitions.Where(x => x.Name.ToLower().Contains(term) || x.Reference.ToLower().Contains(term));
        }
        if (!string.IsNullOrEmpty(status))
        {
            frameworks = Enum.TryParse<LeasingFrameworkStatus>(status, out var fs) && Enum.IsDefined(fs)
                ? frameworks.Where(x => x.Status == fs) : frameworks.Where(x => false);
            acquisitions = Enum.TryParse<LeasingAcquisitionStatus>(status, out var acs) && Enum.IsDefined(acs)
                ? acquisitions.Where(x => x.Status == acs) : acquisitions.Where(x => false);
        }
        if (kind is "orders" or "acquisitions") frameworks = frameworks.Where(x => false);
        if (kind == "frameworks") acquisitions = acquisitions.Where(x => false);
        // Project a common shape before paging; no financial totals from inaccessible agreements.
        var frows = frameworks.Select(x => new { x.Id, Framework = true, x.Name, Number = x.Number, Status = (int)x.Status,
            x.OwnerUserId, Start = x.AcquisitionFrom, End = x.AcquisitionTo, x.Currency, Amount = x.Limit });
        var arows = acquisitions.Select(x => new { x.Id, Framework = false, x.Name, Number = x.Reference, Status = (int)x.Status,
            x.OwnerUserId, Start = x.PurchaseDate, End = x.EndDate, x.Currency, Amount = x.GrossTotal - x.CreditNetTotal - x.CreditVatTotal - x.ReversedNetTotal - x.ReversedVatTotal });
        var query = frows.Concat(arows);
        var count = await query.CountAsync(ct);
        const int size = 25;
        page = Math.Clamp(page, 1, Math.Max(1, (count + size - 1) / size));
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var names = await db.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, ct);
        return new(rows.Select(x => new LeasingRow(x.Id, x.Framework, x.Name, x.Number,
            x.Framework ? ((LeasingFrameworkStatus)x.Status).ToString() : ((LeasingAcquisitionStatus)x.Status).ToString(),
            names.GetValueOrDefault(x.OwnerUserId, ""), x.Start, x.End, x.Currency, x.Amount)).ToList(), count, page, size);
    }

    public async Task<LeasingDetails> GetAsync(Guid account, Guid id, bool framework, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        var orderApprover = await authorization.CanApproveLeasingOrdersAsync(ct);
        var readAdmin = admin || orderApprover || await authorization.CanChangeLeasingLimitAsync(ct);
        LeasingFramework? f = null; LeasingAcquisition? a = null;
        if (framework) f = await db.LeasingFrameworks.Where(x => x.AccountId == account && (readAdmin || x.OwnerUserId == user || db.LeasingOrders.Any(o => o.AccountId == account && o.FrameworkId == x.Id && o.OwnerUserId == user))).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        else a = await WithClassifications(ReadAcquisitions(db, account, user, admin, orderApprover).AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (a != null) a.Items = a.Items.OrderBy(x => x.Position).ToList();
        var finance = f?.FinanceOrganizationId ?? a!.FinanceOrganizationId;
        var owner = f?.OwnerUserId ?? a!.OwnerUserId;
        var documents = await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account &&
            (framework ? x.FrameworkId == id : x.AcquisitionId == id)).OrderByDescending(x => x.UploadedUtc).ToListAsync(ct);
        var history = await (from h in db.LeasingHistory.AsNoTracking()
            join u in db.Users on h.ActorUserId equals u.Id
            where h.AccountId == account && (framework ? h.FrameworkId == id : h.AcquisitionId == id)
            orderby h.RecordedUtc descending
            select new LeasingHistoryRow(h.RecordedUtc, u.FirstName + " " + u.LastName, h.Action, h.Reason, h.BeforeJson, h.AfterJson)).ToListAsync(ct);
        var children = framework ? await ReadAcquisitions(db, account, user, admin, orderApprover).AsNoTracking().Where(x => x.FrameworkId == id)
            .OrderBy(x => x.PurchaseDate).ThenBy(x => x.Name).ToListAsync(ct) : [];
        var referenceNames = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        foreach (var member in await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account)
            .Select(x => new { x.UserId, Name = x.User.FirstName + " " + x.User.LastName }).ToListAsync(ct)) referenceNames[member.UserId] = member.Name;
        foreach (var frame in await Frameworks(db, account, user, admin).AsNoTracking().Select(x => new { x.Id, x.Name }).ToListAsync(ct)) referenceNames[frame.Id] = frame.Name;
        return new(f, a,
            await db.Organizations.Where(x => x.AccountId == account && x.Id == finance).Select(x => x.Name).SingleAsync(ct),
            await db.Users.Where(x => x.Id == owner).Select(x => x.FirstName + " " + x.LastName).SingleAsync(ct),
            a == null ? null : await db.Organizations.Where(x => x.AccountId == account && x.Id == a.SupplierOrganizationId).Select(x => x.Name).SingleAsync(ct),
            a?.FrameworkId == null ? null : await db.LeasingFrameworks.Where(x => x.AccountId == account && x.Id == a.FrameworkId).Select(x => x.Name).SingleAsync(ct),
            f == null ? 0 : (await Capacity(db, f, ct)).Used,
            f != null ? await Frameworks(db, account, user, admin).AnyAsync(x => x.Id == id, ct) : a?.Status != LeasingAcquisitionStatus.Cancelled && await Acquisitions(db, account, user, admin).AnyAsync(x => x.Id == id, ct), children, documents, history, referenceNames, f == null ? 0 : await Reserved(db, account, f.Id, f.IncludesVat, ct));
    }

    public async Task<LeasingAcquisition> NewAcquisitionAsync(Guid account, Guid? frameworkId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        var today = AgreementReminderSchedule.Today(clock.GetUtcNow(), (await db.AgreementReminderSettings.AsNoTracking()
            .Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct)) ?? "Europe/Oslo");
        var result = new LeasingAcquisition { OwnerUserId = user, PurchaseDate = today, Items = [new LeasingItem()] };
        if (frameworkId.HasValue)
        {
            var f = await Frameworks(db, account, user, admin).AsNoTracking().SingleOrDefaultAsync(x => x.Id == frameworkId, ct) ?? throw new UnauthorizedAccessException();
            if (f.Status != LeasingFrameworkStatus.Open) throw new LeasingValidationException("LeasingClosed");
            result.FrameworkId = f.Id; result.FinanceOrganizationId = f.FinanceOrganizationId;
            result.Currency = f.Currency; result.Terms = f.Terms.Copy();
        }
        else if (!admin) throw new UnauthorizedAccessException();
        return result;
    }

    // Serialize every leasing mutation on the account, matching existing agreement financial writes.
    // Always lock before reading amounts/revisions. This also handles moves between two frameworks
    // without deadlocks; a registered acquisition is counted exactly once after the transaction commits.
    private static Task Lock(TenantPlatformDbContext db, Guid account, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);

    public async Task<Guid> SaveFrameworkAsync(Guid account, Guid? id, LeasingFramework input, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var entity = id.HasValue ? await Frameworks(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new UnauthorizedAccessException() : new LeasingFramework { Id = Guid.NewGuid(), AccountId = account };
        if (!id.HasValue && !admin) throw new UnauthorizedAccessException();
        CheckRevision(entity.Revision, input.Revision, id.HasValue); Reason(reason, id.HasValue);
        if (!admin && id.HasValue && entity.OwnerUserId != input.OwnerUserId) throw new UnauthorizedAccessException();
        await References(db, account, input.FinanceOrganizationId, input.OwnerUserId, ct);
        ValidateCommon(input.Name, input.Number, input.Currency, input.Notes, input.Terms);
        if (!Enum.IsDefined(input.Status) || input.AcquisitionFrom == default || input.AcquisitionTo < input.AcquisitionFrom ||
            input.Limit < 0 || input.Limit > 1000000000000m || !Precision(input.Limit, 2)) throw new LeasingValidationException("LeasingInvalidFramework");
        var registered = await db.LeasingAcquisitions.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == entity.Id &&
            x.Status == LeasingAcquisitionStatus.Registered).ToListAsync(ct);
        if (registered.Any(x => x.Currency != input.Currency || x.FinanceOrganizationId != input.FinanceOrganizationId ||
            !LeasingCalculator.InPeriod(x.PurchaseDate, input.AcquisitionFrom, input.AcquisitionTo)) ||
            (registered.Count > 0 && entity.IncludesVat != input.IncludesVat)) throw new LeasingValidationException("LeasingFrameworkConflict");
        if (id.HasValue && entity.Limit != input.Limit) throw new LeasingValidationException("LeasingLimitProposalRequired");
        if (await db.LeasingOrders.AnyAsync(x => x.AccountId == account && x.FrameworkId == entity.Id, ct) && (entity.Currency != input.Currency || entity.IncludesVat != input.IncludesVat)) throw new LeasingValidationException("LeasingFrameworkConflict");
        if (LeasingCalculator.Used(registered, input.IncludesVat) + await Reserved(db, account, entity.Id, input.IncludesVat, ct) > input.Limit) throw new LeasingValidationException("LeasingLimitExceeded");
        var before = id.HasValue ? Snapshot(entity) : "{}";
        entity.Name = input.Name.Trim(); entity.Number = input.Number.Trim(); entity.FinanceOrganizationId = input.FinanceOrganizationId;
        entity.OwnerUserId = input.OwnerUserId; entity.AcquisitionFrom = input.AcquisitionFrom; entity.AcquisitionTo = input.AcquisitionTo;
        if (entity.CreditNotesReleaseLimit != input.CreditNotesReleaseLimit && await db.LeasingInvoices.AnyAsync(x => x.AccountId == account && x.Kind == LeasingInvoiceKind.CreditNote && x.Status == LeasingInvoiceStatus.Approved && db.LeasingAcquisitions.Any(a => a.AccountId == account && a.Id == x.AcquisitionId && a.FrameworkId == entity.Id), ct)) throw new LeasingValidationException("InvoiceCreditRuleLocked");
        entity.CreditNotesReleaseLimit = input.CreditNotesReleaseLimit;
        entity.Limit = input.Limit; entity.Currency = input.Currency; entity.IncludesVat = input.IncludesVat;
        entity.Terms = input.Terms.Copy(); entity.Notes = input.Notes?.Trim(); entity.Status = input.Status; entity.Revision = Guid.NewGuid();
        if (!id.HasValue) db.LeasingFrameworks.Add(entity);
        History(db, account, entity.Id, null, user, id.HasValue ? "Updated" : "Created", reason, before, Snapshot(entity));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return entity.Id;
    }

    public async Task<Guid> SaveAcquisitionAsync(Guid account, Guid? id, LeasingAcquisition input, string reason, CancellationToken ct = default, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications = null, OrderDelivery? delivery = null)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        if (delivery != null && await OrderRetry(db, account, delivery.OrderId, delivery.RequestId, user, "Realized", ct))
            return await db.LeasingOrderRealizations.Where(x => x.AccountId == account && x.OrderId == delivery.OrderId && x.RequestId == delivery.RequestId).Select(x => x.AcquisitionId).FirstAsync(ct);
        var result = await SaveAcquisitionCore(db, account, user, admin, id, input, reason, ct, classifications, delivery: delivery);
        await tx.CommitAsync(ct); return result;
    }
    private async Task<Guid> SaveAcquisitionCore(TenantPlatformDbContext db, Guid account, Guid user, bool admin, Guid? id,
        LeasingAcquisition input, string reason, CancellationToken ct, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications = null, bool invoiceApproval = false, OrderDelivery? delivery = null)
    {
        var entity = id.HasValue ? await WithClassifications(Acquisitions(db, account, user, admin)).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new UnauthorizedAccessException() : new LeasingAcquisition { Id = Guid.NewGuid(), AccountId = account };
        CheckRevision(entity.Revision, input.Revision, id.HasValue); Reason(reason, id.HasValue);
        if (entity.Status == LeasingAcquisitionStatus.Cancelled || input.Status == LeasingAcquisitionStatus.Cancelled ||
            !Enum.IsDefined(input.Status) || (entity.Status == LeasingAcquisitionStatus.Registered && input.Status != LeasingAcquisitionStatus.Registered))
            throw new LeasingValidationException("LeasingInvalidTransition");
        if (!admin && id.HasValue && entity.OwnerUserId != input.OwnerUserId) throw new UnauthorizedAccessException();
        LeasingFramework? framework = null;
        if (input.FrameworkId.HasValue)
        {
            framework = await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == input.FrameworkId, ct)
                ?? throw new UnauthorizedAccessException();
            // A caller cannot attach a purchase to an inaccessible framework, even within the same tenant.
            if (!admin && framework.OwnerUserId != user && (!id.HasValue || entity.FrameworkId != framework.Id) &&
                !(delivery != null && await Orders(db, account, user, false).AnyAsync(o => o.Id == delivery.OrderId && o.FrameworkId == framework.Id && o.Status == LeasingOrderStatus.Approved, ct))) throw new UnauthorizedAccessException();
            if (input.Currency != framework.Currency || input.FinanceOrganizationId != framework.FinanceOrganizationId)
                throw new LeasingValidationException("LeasingFrameworkCurrency");
        }
        else if (!id.HasValue && !admin) throw new UnauthorizedAccessException();
        await References(db, account, input.FinanceOrganizationId, input.OwnerUserId, ct);
        if (!await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == input.SupplierOrganizationId, ct)) throw new LeasingValidationException("LeasingInvalidParty");
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
        if (id.HasValue && await db.LeasingInvoices.AnyAsync(x => x.AccountId == account && x.AcquisitionId == id && x.Status == LeasingInvoiceStatus.Approved, ct) &&
            (entity.FrameworkId != input.FrameworkId || entity.SupplierOrganizationId != input.SupplierOrganizationId || entity.Currency != input.Currency)) throw new LeasingValidationException("InvoiceLinkedPurchase");
        if (id.HasValue)
        {
            var linked = await db.LeasingOrderRealizations.Where(x => x.AccountId == account && x.AcquisitionId == entity.Id).ToListAsync(ct);
            if (linked.Count > 0 && (input.FrameworkId != entity.FrameworkId || input.Currency != entity.Currency || input.SupplierOrganizationId != entity.SupplierOrganizationId ||
                linked.Any(l => { var old = entity.Items.Single(x => x.Id == l.ItemId); var next = input.Items.SingleOrDefault(x => x.Id == l.ItemId); return next == null || next.Quantity != old.Quantity || next.UnitPrice != old.UnitPrice || next.VatPercent != old.VatPercent; }))) throw new LeasingValidationException("LeasingOrderLinkedImmutable");
        }
        if (id.HasValue && await db.LeasingFinancingRevisions.AnyAsync(x => x.AccountId == account && x.AcquisitionId == entity.Id, ct) &&
            Snapshot(LeasingFinancingSnapshot.From(entity)) != Snapshot(LeasingFinancingSnapshot.From(input))) throw new LeasingValidationException("PaymentUseFinancingRevision");
        var retainedIds = input.Items.Select(x => x.Id).ToArray();
        if (id.HasValue && await db.LeasingInvoiceLines.AnyAsync(x => x.AccountId == account && !retainedIds.Contains(x.ItemId) &&
            db.LeasingItems.Any(i => i.AccountId == account && i.AcquisitionId == id && i.Id == x.ItemId), ct)) throw new LeasingValidationException("InvoiceLinkedPurchase");
        if (id.HasValue)
        {
            var reversedItemIds = await db.LeasingInvoiceLines.Where(x => x.AccountId == account && x.CreatedItem &&
                db.LeasingInvoices.Any(d => d.AccountId == account && d.Id == x.InvoiceId && d.AcquisitionId == id && d.Status == LeasingInvoiceStatus.Reversed)).Select(x => x.ItemId).ToListAsync(ct);
            foreach (var item in input.Items.Where(x => reversedItemIds.Contains(x.Id)))
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
        var catalog = await Catalog(db, account, admin, ct);
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
        foreach (var removed in entity.Items.Where(x => !incomingIds.Contains(x.Id)).ToList()) { RemoveClassification(db, removed); db.LeasingItems.Remove(removed); entity.Items.Remove(removed); }
        for (var position = 0; position < input.Items.Count; position++)
        {
            var incoming = input.Items[position];
            var item = incoming.Id == Guid.Empty ? new LeasingItem { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = entity.Id } : entity.Items.Single(x => x.Id == incoming.Id);
            item.Position = position; item.Description = incoming.Description.Trim(); item.ItemNumber = incoming.ItemNumber?.Trim();
            item.Quantity = incoming.Quantity; item.UnitPrice = incoming.UnitPrice; item.VatPercent = incoming.VatPercent;
            item.InvoiceNetAdjustment = incoming.InvoiceNetAdjustment; item.InvoiceVatAdjustment = incoming.InvoiceVatAdjustment;
            if (incoming.Id == Guid.Empty) { entity.Items.Add(item); if (id.HasValue) db.LeasingItems.Add(item); }
            if (classifications != null && classifications.TryGetValue(position, out var classification))
                ApplyClassification(db, item, classification, catalog, input.Status == LeasingAcquisitionStatus.Registered);
            else if (registering || incoming.Id == Guid.Empty)
                ApplyClassification(db, item, LeasingClassificationInput.From(item), catalog, input.Status == LeasingAcquisitionStatus.Registered);
            if (input.Status == LeasingAcquisitionStatus.Registered && !LeasingAllocationCalculator.Calculate(item).Complete)
                throw new LeasingValidationException("LeasingInvalidAllocation");
        }
        if (!id.HasValue) db.LeasingAcquisitions.Add(entity);
        History(db, account, null, entity.Id, user, id.HasValue ? "Updated" : "Created", reason, before, Snapshot(entity));
        await db.SaveChangesAsync(ct);
        await EnsureInitialFinancing(db, entity, user, ct); await db.SaveChangesAsync(ct);
        if (delivery != null) { await ConsumeOrder(db, account, user, admin, entity, delivery, ct); await db.SaveChangesAsync(ct); }
        if (framework != null && entity.Status == LeasingAcquisitionStatus.Registered) await CheckCapacity(db, framework, ct);
        return entity.Id;
    }

    public async Task CancelAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var a = await WithClassifications(Acquisitions(db, account, user, admin)).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(a.Revision, revision, true); Reason(reason, true);
        if (a.Status == LeasingAcquisitionStatus.Cancelled) throw new LeasingValidationException("LeasingInvalidTransition");
        await ReverseOrderParts(db, a, user, a.Items.Select(x => x.Id), ct);
        var before = Snapshot(a); a.Status = LeasingAcquisitionStatus.Cancelled; a.Revision = Guid.NewGuid();
        History(db, account, null, id, user, "Cancelled", reason, before, Snapshot(a));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private static void CheckRevision(Guid actual, Guid supplied, bool existing)
    { if (existing && actual != supplied) throw new LeasingValidationException("LeasingConcurrency"); }
    private static bool Precision(decimal value, int places) => decimal.Round(value, places) == value;
    private static void Reason(string? value, bool required)
    { if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired"); }
    private static async Task References(TenantPlatformDbContext db, Guid account, Guid party, Guid owner, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == party, ct)) throw new LeasingValidationException("LeasingInvalidParty");
        if (!await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == owner && x.User.IsActive, ct)) throw new LeasingValidationException("LeasingInvalidOwner");
    }
    private static void ValidateCommon(string name, string number, string currency, string? notes, LeasingTerms terms)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(number) || number.Length > 100 || notes?.Length > 10000 ||
            !AgreementPeriodCalculator.Currencies.Contains(currency)) throw new LeasingValidationException("LeasingInvalidDetails");
        if (terms.Months is < 1 or > 600 || !Enum.IsDefined(terms.InterestKind) || !Enum.IsDefined(terms.PaymentFrequency)) throw new LeasingValidationException("LeasingInvalidTerms");
        if (terms.InterestKind == LeasingInterestKind.Reference)
        {
            if (string.IsNullOrWhiteSpace(terms.ReferenceRateName) || terms.ReferenceRateName.Length > 100 || terms.MarginPercentagePoints is null or < -100 or > 100 ||
                !Precision(terms.MarginPercentagePoints.Value, 4)) throw new LeasingValidationException("LeasingInvalidTerms");
            terms.AnnualRatePercent = null;
        }
        else
        {
            if (terms.AnnualRatePercent is null or < 0 or > 100 || !Precision(terms.AnnualRatePercent.Value, 4)) throw new LeasingValidationException("LeasingInvalidTerms");
            terms.ReferenceRateName = null; terms.MarginPercentagePoints = null;
        }
    }
    private static string Snapshot<T>(T entity) => JsonSerializer.Serialize(entity);
    private void History(TenantPlatformDbContext db, Guid account, Guid? framework, Guid? acquisition, Guid actor, string action, string reason, string before, string after) =>
        db.LeasingHistory.Add(new LeasingHistory { Id = Guid.NewGuid(), AccountId = account, FrameworkId = framework, AcquisitionId = acquisition,
            ActorUserId = actor, RecordedUtc = clock.GetUtcNow(), Action = action, Reason = reason?.Trim() ?? "", BeforeJson = before, AfterJson = after });

    public async Task UploadAsync(Guid account, Guid parentId, bool framework, Guid revision, string fileName, Stream content, CancellationToken ct = default)
    {
        var details = await GetAsync(account, parentId, framework, ct);
        if (!details.CanEdit) throw new UnauthorizedAccessException();
        CheckRevision(details.Framework?.Revision ?? details.Acquisition!.Revision, revision, true);
        var stored = await storage.StoreAsync(content, fileName, ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var (user, admin) = await Member(db, account, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
            if (framework)
            {
                var f = await Frameworks(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == parentId, ct) ?? throw new UnauthorizedAccessException();
                CheckRevision(f.Revision, revision, true); f.Revision = Guid.NewGuid();
            }
            else
            {
                var a = await Acquisitions(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == parentId, ct) ?? throw new UnauthorizedAccessException();
                if (a.Status == LeasingAcquisitionStatus.Cancelled) throw new UnauthorizedAccessException();
                CheckRevision(a.Revision, revision, true); a.Revision = Guid.NewGuid();
            }
            var doc = new LeasingDocument { Id = Guid.NewGuid(), AccountId = account,
                FrameworkId = framework ? parentId : null, AcquisitionId = framework ? null : parentId,
                FileName = stored.FileName, MediaType = stored.MediaType, StorageKey = stored.StorageKey, Size = stored.Size,
                UploadedByUserId = user, UploadedUtc = clock.GetUtcNow() };
            db.LeasingDocuments.Add(doc);
            History(db, account, doc.FrameworkId, doc.AcquisitionId, user, "DocumentAdded", stored.FileName, "{}", Snapshot(new { doc.Id, doc.FileName, doc.Size }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch
        {
            try
            {
                await using var check = await factory.CreateDbContextAsync(CancellationToken.None);
                if (!await check.LeasingDocuments.AnyAsync(x => x.AccountId == account && x.StorageKey == stored.StorageKey))
                    await storage.DiscardUncommittedAsync(stored.StorageKey);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not clean up leasing file {StorageKey}", stored.StorageKey); }
            throw;
        }
    }
    public async Task<AgreementDownload> DownloadAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        var doc = await db.LeasingDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        var allowed = doc.OrderId.HasValue ? await Orders(db, account, user, await OrderReader(admin, ct)).AnyAsync(x => x.Id == doc.OrderId, ct) : doc.FrameworkId.HasValue
            ? await Frameworks(db, account, user, admin).AnyAsync(x => x.Id == doc.FrameworkId, ct)
            : await Acquisitions(db, account, user, await PaymentReader(admin, ct)).AnyAsync(x => x.Id == doc.AcquisitionId, ct);
        if (!allowed) throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(doc.StorageKey, ct), doc.FileName, doc.MediaType);
    }
}
