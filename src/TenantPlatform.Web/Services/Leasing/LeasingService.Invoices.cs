using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Leasing.Invoices;

namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    private static IQueryable<LeasingInvoice> Invoices(TenantPlatformDbContext db, Guid account, Guid user, bool admin)
    {
        var allowed = Acquisitions(db, account, user, false).Select(x => x.Id);
        return db.LeasingInvoices.Where(x => x.AccountId == account && (admin || x.AcquisitionId == null && x.UploadedByUserId == user || allowed.Contains(x.AcquisitionId ?? Guid.Empty)));
    }
    public async Task<(List<LeasingInvoice> Documents, List<LeasingInvoiceLine> Lines)> PurchaseInvoicesAsync(Guid account, Guid acquisition, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (!await Acquisitions(db, account, user, admin).AnyAsync(x => x.Id == acquisition, ct)) throw new UnauthorizedAccessException();
        var docs = await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).OrderByDescending(x => x.UploadedUtc).ToListAsync(ct);
        var ids = docs.Select(x => x.Id).ToArray();
        return (docs, await db.LeasingInvoiceLines.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.InvoiceId)).ToListAsync(ct));
    }
    public async Task<List<LeasingInvoice>> ListInvoicesAsync(Guid account, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct); return await Invoices(db, account, user, admin).AsNoTracking().OrderByDescending(x => x.UploadedUtc).Take(500).ToListAsync(ct); }
    public async Task<Guid> UploadInvoiceAsync(Guid account, Guid? acquisition, string fileName, Stream content, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        if (acquisition.HasValue) { if (!await Acquisitions(db, account, user, admin).AnyAsync(x => x.Id == acquisition && x.Status != LeasingAcquisitionStatus.Cancelled, ct)) throw new UnauthorizedAccessException(); }
        else if (!admin && !await Acquisitions(db, account, user, false).AnyAsync(ct) && !await Frameworks(db, account, user, false).AnyAsync(ct)) throw new UnauthorizedAccessException();
        if (Path.GetExtension(fileName).ToLowerInvariant() is not (".xml" or ".pdf" or ".png" or ".jpg" or ".jpeg")) throw new AgreementFileException("AgreementInvalidFileType");
        var stored = await storage.StoreAsync(content, fileName, ct);
        try
        {
            await using var original = await storage.OpenReadAsync(stored.StorageKey, ct); var hash = Convert.ToHexString(await SHA256.HashDataAsync(original, ct));
            var invoice = new LeasingInvoice { Id = Guid.NewGuid(), AccountId = account, AcquisitionId = acquisition, UploadedByUserId = user, UploadedUtc = clock.GetUtcNow(),
                FileName = stored.FileName, StorageKey = stored.StorageKey, MediaType = stored.MediaType, Size = stored.Size, FileHash = hash, Revision = Guid.NewGuid() };
            db.LeasingInvoices.Add(invoice); InvoiceHistory(db, invoice, user, "Uploaded", "", "{}", "{}"); await db.SaveChangesAsync(ct); return invoice.Id;
        }
        catch
        {
            await using var check = await factory.CreateDbContextAsync(CancellationToken.None);
            if (!await check.LeasingInvoices.AnyAsync(x => x.AccountId == account && x.StorageKey == stored.StorageKey)) await storage.DiscardUncommittedAsync(stored.StorageKey);
            throw;
        }
    }
    public async Task<InvoiceDetails> GetInvoiceAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var invoice = await Invoices(db, account, user, admin).AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        var review = ReadReview(invoice); var acquisition = invoice.AcquisitionId.HasValue ? await WithClassifications(Acquisitions(db, account, user, admin).AsNoTracking()).SingleAsync(x => x.Id == invoice.AcquisitionId, ct) : null;
        var documents = invoice.AcquisitionId.HasValue ? await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == invoice.AcquisitionId).ToListAsync(ct) : [];
        var ids = documents.Where(x => x.Status == LeasingInvoiceStatus.Approved).Select(x => x.Id).ToArray();
        var lines = await db.LeasingInvoiceLines.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.InvoiceId)).ToListAsync(ct);
        var duplicates = await DuplicateWarnings(db, invoice, review.Data, ct);
        return new(invoice, review,
            await db.LeasingInvoiceInterpretations.AsNoTracking().Where(x => x.AccountId == account && x.InvoiceId == id).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct),
            await db.LeasingInvoiceHistory.AsNoTracking().Where(x => x.AccountId == account && x.InvoiceId == id).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct),
            await Acquisitions(db, account, user, admin).AsNoTracking().Where(x => x.Status != LeasingAcquisitionStatus.Cancelled).OrderBy(x => x.Name).Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct), acquisition, lines, documents, duplicates);
    }
    public async Task<AgreementDownload> DownloadInvoiceAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var invoice = await Invoices(db, account, user, admin).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(invoice.StorageKey, ct), invoice.FileName, invoice.MediaType);
    }
    private static InvoiceReview ReadReview(LeasingInvoice invoice) => JsonSerializer.Deserialize<InvoiceReview>(invoice.ReviewJson) ?? new();
    private async Task<LeasingInvoice> EditableInvoice(TenantPlatformDbContext db, Guid account, Guid id, Guid revision, Guid user, bool admin, CancellationToken ct)
    {
        var invoice = await Invoices(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        CheckRevision(invoice.Revision, revision, true);
        if (invoice.Status != LeasingInvoiceStatus.Review) throw new LeasingValidationException("InvoiceImmutable");
        return invoice;
    }
    public async Task SaveInvoiceReviewAsync(Guid account, Guid id, Guid revision, InvoiceReview review, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var invoice = await EditableInvoice(db, account, id, revision, user, admin, ct);
        if (review.AcquisitionId.HasValue)
        {
            review.AcquisitionRevision = await Acquisitions(db, account, user, admin).Where(x => x.Id == review.AcquisitionId && x.Status != LeasingAcquisitionStatus.Cancelled).Select(x => (Guid?)x.Revision).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
        }
        else review.AcquisitionRevision = null;
        if (!review.AcquisitionId.HasValue && invoice.UploadedByUserId != user && !admin) throw new UnauthorizedAccessException();
        if (review.Data.Lines.Count > 500 || Snapshot(review).Length > 2_000_000 || review.ReviewReason.Length > 2000 || review.DuplicateOverrideReason.Length > 2000) throw new LeasingValidationException("InvoiceInvalidReview");
        var before = invoice.ReviewJson; invoice.ReviewJson = Snapshot(review); invoice.AcquisitionId = review.AcquisitionId;
        invoice.ReviewedByUserId = user; invoice.ReviewedUtc = clock.GetUtcNow(); invoice.Revision = Guid.NewGuid();
        InvoiceHistory(db, invoice, user, "Reviewed", review.ReviewReason, before, invoice.ReviewJson);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RetryInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var invoice = await EditableInvoice(db, account, id, revision, user, admin, ct);
        if (invoice.Processing is LeasingInvoiceProcessing.Processing or LeasingInvoiceProcessing.Uploaded) throw new LeasingValidationException("InvoiceProcessingBusy");
        invoice.Processing = LeasingInvoiceProcessing.Uploaded; invoice.ProcessingError = null; invoice.Revision = Guid.NewGuid();
        InvoiceHistory(db, invoice, user, "Retry", "", "{}", "{}"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task UseInterpretationAsync(Guid account, Guid id, Guid revision, Guid interpretationId, CancellationToken ct = default)
    {
        var details = await GetInvoiceAsync(account, id, ct); var result = details.Interpretations.SingleOrDefault(x => x.Id == interpretationId) ?? throw new UnauthorizedAccessException();
        var review = details.Review; review.Data = JsonSerializer.Deserialize<InvoiceData>(result.ResultJson)!; review.Matches = []; review.ContentConfirmed = false;
        review.ReviewReason = "Use interpretation " + result.Version;
        await SaveInvoiceReviewAsync(account, id, revision, review, ct);
    }
    public async Task RejectInvoiceAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var invoice = await EditableInvoice(db, account, id, revision, user, admin, ct); Reason(reason, true);
        invoice.Status = LeasingInvoiceStatus.Rejected; invoice.Revision = Guid.NewGuid();
        InvoiceHistory(db, invoice, user, "Rejected", reason, invoice.ReviewJson, invoice.ReviewJson);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private static string Identity(InvoiceData data) => string.IsNullOrWhiteSpace(data.SupplierNumber) ? "" : new string(data.SupplierNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    private static string InvoiceNumber(InvoiceData data) => data.Number.Trim().ToUpperInvariant();
    private static async Task<List<string>> DuplicateWarnings(TenantPlatformDbContext db, LeasingInvoice invoice, InvoiceData data, CancellationToken ct)
    {
        var warnings = new List<string>(); var identity = Identity(data); var number = InvoiceNumber(data);
        var others = db.LeasingInvoices.Where(x => x.AccountId == invoice.AccountId && x.Id != invoice.Id && x.Status == LeasingInvoiceStatus.Approved);
        if (await others.AnyAsync(x => x.FileHash == invoice.FileHash || identity != "" && x.SupplierIdentity == identity && x.Kind == data.Kind && x.Number == number, ct)) warnings.Add("InvoiceCertainDuplicate");
        if (await others.AnyAsync(x => x.Kind == data.Kind && (x.Number == number && number != "" || x.InvoiceDate == data.Date && x.Currency == data.Currency && x.Net == data.Net && x.Vat == data.Vat), ct)) warnings.Add("InvoicePossibleDuplicate");
        return warnings;
    }
    public async Task<InvoiceApprovalPreview> PreviewInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        var invoice = await EditableInvoice(db, account, id, revision, user, admin, ct); var review = ReadReview(invoice);
        var a = review.AcquisitionId.HasValue ? await Acquisitions(db, account, user, admin).AsNoTracking().SingleOrDefaultAsync(x => x.Id == review.AcquisitionId, ct) ?? throw new UnauthorizedAccessException() : review.NewAcquisition;
        var amounts = InvoiceCalculator.Calculate(review.Data); var f = a.FrameworkId.HasValue ? await Frameworks(db, account, user, admin).AsNoTracking().SingleOrDefaultAsync(x => x.Id == a.FrameworkId, ct) : null;
        // Framework owner access is not required to view the limit of an already accessible acquisition.
        if (a.FrameworkId.HasValue && f == null && review.AcquisitionId.HasValue) f = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.AccountId == account && x.Id == a.FrameworkId, ct);
        if (a.FrameworkId.HasValue && f == null) throw new UnauthorizedAccessException();
        var current = review.AcquisitionId.HasValue ? a.GrossTotal - a.CreditNetTotal - a.CreditVatTotal - a.ReversedNetTotal - a.ReversedVatTotal : 0;
        decimal changeNet = 0, changeVat = 0;
        for (int i = 0; i < review.Data.Lines.Count; i++)
            if (review.Data.Kind == LeasingInvoiceKind.CreditNote || review.Matches.SingleOrDefault(x => x.ReviewLineId == review.Data.Lines[i].ReviewId)?.ItemId == null)
            { changeNet += amounts.LineNet[i]; changeVat += amounts.LineVat[i]; }
        if (review.Data.Kind == LeasingInvoiceKind.CreditNote) { changeNet = -changeNet; changeVat = -changeVat; }
        var used = f == null ? 0 : await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.FrameworkId == f.Id && x.Status == LeasingAcquisitionStatus.Registered).SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct);
        var delta = review.Data.Kind == LeasingInvoiceKind.CreditNote && f?.CreditNotesReleaseLimit != true ? 0 : changeNet + (f?.IncludesVat == true ? changeVat : 0);
        if (a.Status == LeasingAcquisitionStatus.Draft && review.Data.Kind == LeasingInvoiceKind.Invoice) delta += f?.IncludesVat == true ? a.GrossTotal : a.NetTotal;
        return new(current, changeNet + changeVat, current + changeNet + changeVat, used, used + (f == null ? 0 : delta), f?.Limit, f?.CreditNotesReleaseLimit);
    }
    public async Task<Guid> ApproveInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var invoice = await Invoices(db, account, user, admin).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (invoice.Status == LeasingInvoiceStatus.Approved) return invoice.AcquisitionId!.Value; // idempotent retry
        CheckRevision(invoice.Revision, revision, true);
        if (invoice.Status != LeasingInvoiceStatus.Review || invoice.Processing is LeasingInvoiceProcessing.Uploaded or LeasingInvoiceProcessing.Processing) throw new LeasingValidationException("InvoiceImmutable");
        var review = ReadReview(invoice); var data = review.Data; var amounts = InvoiceCalculator.Calculate(data);
        if (!review.ContentConfirmed || !Enum.IsDefined(data.Kind) || string.IsNullOrWhiteSpace(data.SupplierName) || data.SupplierName.Length > 200 || data.SupplierNumber.Length > 200 ||
            string.IsNullOrWhiteSpace(data.Number) || data.Number.Length > 100 || data.Date is null || data.Date == DateOnly.MinValue || !AgreementPeriodCalculator.Currencies.Contains(data.Currency)) throw new LeasingValidationException("InvoiceReviewRequired");
        if (amounts.Errors.Count > 0) throw new LeasingValidationException(amounts.Errors[0]);
        var duplicates = await DuplicateWarnings(db, invoice, data, ct);
        if (duplicates.Contains("InvoiceCertainDuplicate")) throw new LeasingValidationException("InvoiceCertainDuplicate");
        if (duplicates.Contains("InvoicePossibleDuplicate") && string.IsNullOrWhiteSpace(review.DuplicateOverrideReason)) throw new LeasingValidationException("InvoiceDuplicateReason");
        if (review.Matches.Select(x => x.ReviewLineId).Distinct().Count() != review.Matches.Count || data.Lines.Select(x => x.ReviewId).Distinct().Count() != data.Lines.Count ||
            data.Lines.Any(x => review.Matches.All(m => m.ReviewLineId != x.ReviewId))) throw new LeasingValidationException("InvoiceMatchRequired");
        LeasingAcquisition a;
        if (review.AcquisitionId.HasValue) a = await WithClassifications(Acquisitions(db, account, user, admin)).SingleOrDefaultAsync(x => x.Id == review.AcquisitionId, ct) ?? throw new UnauthorizedAccessException();
        else
        {
            if (data.Kind == LeasingInvoiceKind.CreditNote || !review.PurchaseDateConfirmed) throw new LeasingValidationException("InvoicePurchaseDateConfirm");
            a = JsonSerializer.Deserialize<LeasingAcquisition>(Snapshot(review.NewAcquisition))!; a.Items = []; a.Id = Guid.Empty;
        }
        if (review.AcquisitionId.HasValue && review.AcquisitionRevision != a.Revision) throw new LeasingValidationException("LeasingConcurrency");
        if (a.Status == LeasingAcquisitionStatus.Cancelled || a.Currency != data.Currency) throw new LeasingValidationException("InvoicePurchaseConflict");
        var party = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == a.SupplierOrganizationId, ct) ?? throw new LeasingValidationException("LeasingInvalidParty");
        if (!string.IsNullOrWhiteSpace(party.OrganizationNumber) && Identity(data) != new string(party.OrganizationNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()) throw new LeasingValidationException("InvoiceSupplierMismatch");
        var financialBefore = review.AcquisitionId.HasValue ? Snapshot(a) : "{}";
        var input = JsonSerializer.Deserialize<LeasingAcquisition>(Snapshot(a))!; input.Status = LeasingAcquisitionStatus.Registered;
        var originals = await db.LeasingInvoiceLines.AsNoTracking().Where(x => x.AccountId == account && db.LeasingInvoices.Any(d => d.AccountId == account && d.Id == x.InvoiceId && d.AcquisitionId == a.Id && d.Status == LeasingInvoiceStatus.Approved)).ToListAsync(ct);
        var docs = await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == a.Id && x.Status == LeasingInvoiceStatus.Approved).ToDictionaryAsync(x => x.Id, ct);
        var classifications = new Dictionary<int, LeasingClassificationInput>(); var targets = new List<(InvoiceDataLine Source, Guid? Target, int Position, Guid? Credit)>();
        var pending = new List<LeasingInvoiceLine>();
        var reversedItems = await db.LeasingInvoiceLines.Where(x => x.AccountId == account && x.CreatedItem && db.LeasingInvoices.Any(d => d.AccountId == account && d.Id == x.InvoiceId && d.Status == LeasingInvoiceStatus.Reversed)).Select(x => x.ItemId).ToListAsync(ct);
        for (int i = 0; i < data.Lines.Count; i++)
        {
            var line = data.Lines[i]; var match = review.Matches.Single(x => x.ReviewLineId == line.ReviewId); var net = amounts.LineNet[i]; var vat = amounts.LineVat[i]; var qty = line.DocumentAdjustment ? 0 : line.Quantity!.Value;
            if (data.Kind == LeasingInvoiceKind.CreditNote)
            {
                var source = originals.SingleOrDefault(x => x.Id == match.CreditedLineId && docs[x.InvoiceId].Kind == LeasingInvoiceKind.Invoice) ?? throw new LeasingValidationException("InvoiceCreditMatch");
                if (match.ItemId != source.ItemId || Math.Sign(source.Net) != Math.Sign(net)) throw new LeasingValidationException("InvoiceCreditMatch");
                var prior = originals.Concat(pending).Where(x => x.CreditedLineId == source.Id).ToList();
                if (prior.Sum(x => x.Quantity) + qty > source.Quantity || prior.Sum(x => Math.Abs(x.Net)) + Math.Abs(net) > Math.Abs(source.Net) || prior.Sum(x => Math.Abs(x.Vat)) + Math.Abs(vat) > Math.Abs(source.Vat)) throw new LeasingValidationException("InvoiceOverCredit");
                pending.Add(new() { CreditedLineId = source.Id, Quantity = qty, Net = net, Vat = vat });
                targets.Add((line, source.ItemId, -1, source.Id));
            }
            else if (match.ItemId.HasValue)
            {
                var target = a.Items.SingleOrDefault(x => x.Id == match.ItemId) ?? throw new LeasingValidationException("InvoiceMatchRequired");
                if (reversedItems.Contains(target.Id)) throw new LeasingValidationException("InvoiceMatchRequired");
                var documented = originals.Where(x => x.ItemId == target.Id).Select(x => (x, Sign: docs[x.InvoiceId].Kind == LeasingInvoiceKind.Invoice ? 1 : -1)).ToList();
                var targetTotals = LeasingCalculator.Line(target);
                var other = pending.Where(x => x.ItemId == target.Id).ToList();
                if (documented.Sum(x => x.x.Quantity * x.Sign) + other.Sum(x => x.Quantity) + qty > target.Quantity ||
                    Math.Abs(documented.Sum(x => x.x.Net * x.Sign) + other.Sum(x => x.Net) + net) > Math.Abs(targetTotals.Net) ||
                    Math.Abs(documented.Sum(x => x.x.Vat * x.Sign) + other.Sum(x => x.Vat) + vat) > Math.Abs(targetTotals.Vat)) throw new LeasingValidationException("InvoiceOverDocumented");
                pending.Add(new() { ItemId = target.Id, Quantity = qty, Net = net, Vat = vat }); targets.Add((line, target.Id, -1, null));
            }
            else
            {
                var position = input.Items.Count; var item = ImportedItem(line, net, vat); input.Items.Add(item); classifications[position] = match.Classification;
                targets.Add((line, null, position, null));
            }
        }
        if (data.Kind == LeasingInvoiceKind.Invoice)
        {
            var aid = await SaveAcquisitionCore(db, account, user, admin, review.AcquisitionId, input, "Invoice approval " + id, ct, classifications, invoiceApproval: true);
            a = await WithClassifications(db.LeasingAcquisitions.Where(x => x.AccountId == account)).SingleAsync(x => x.Id == aid, ct);
        }
        else
        {
            if (a.Status != LeasingAcquisitionStatus.Registered || a.Items.Any(x => !LeasingAllocationCalculator.Calculate(x).Complete)) throw new LeasingValidationException("LeasingInvalidAllocation");
            if (a.FrameworkId.HasValue)
            {
                var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == account && x.Id == a.FrameworkId, ct);
                if (!f.CreditNotesReleaseLimit.HasValue) throw new LeasingValidationException("InvoiceCreditRuleRequired");
                invoice.ReleasesLimit = f.CreditNotesReleaseLimit.Value;
            }
        }
        invoice.AcquisitionId = a.Id; invoice.Kind = data.Kind; invoice.SupplierIdentity = Identity(data); invoice.Number = InvoiceNumber(data); invoice.InvoiceDate = data.Date;
        invoice.Currency = data.Currency; invoice.Net = amounts.Net; invoice.Vat = amounts.Vat; invoice.Status = LeasingInvoiceStatus.Approved;
        invoice.ApprovedJson = invoice.ReviewJson; invoice.ApprovedByUserId = user; invoice.ApprovedUtc = clock.GetUtcNow(); invoice.Revision = Guid.NewGuid();
        for (int i = 0; i < targets.Count; i++)
        { var t = targets[i]; invoice.Lines.Add(new() { Id = Guid.NewGuid(), AccountId = account, InvoiceId = id, ItemId = t.Target ?? a.Items.Single(x => x.Position == t.Position).Id,
            CreditedLineId = t.Credit, SourceLineId = t.Source.SourceId, Quantity = t.Source.DocumentAdjustment ? 0 : t.Source.Quantity!.Value, Net = amounts.LineNet[i], Vat = amounts.LineVat[i], CreatedItem = !t.Target.HasValue }); }
        foreach (var line in invoice.Lines) db.Entry(line).State = EntityState.Added;
        InvoiceHistory(db, invoice, user, "Approved", review.DuplicateOverrideReason, invoice.ReviewJson, invoice.ApprovedJson);
        await db.SaveChangesAsync(ct); await UpdateInvoiceEffects(db, a, ct);
        History(db, account, null, a.Id, user, "InvoiceApproved", id.ToString(), financialBefore, Snapshot(a));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return a.Id;
    }
    public static LeasingItem ImportedItem(InvoiceDataLine line, decimal net, decimal vat)
    {
        var price = line.DocumentAdjustment ? 0 : decimal.Round((line.Price ?? 0) / line.BaseQuantity, 4, MidpointRounding.AwayFromZero);
        var qty = line.Quantity ?? 1; var rate = line.VatPercent ?? 0;
        return new() { Description = line.Description, ItemNumber = line.ItemNumber, Quantity = qty, UnitPrice = price, VatPercent = rate,
            InvoiceNetAdjustment = net - InvoiceCalculator.Money(qty * price), InvoiceVatAdjustment = vat - InvoiceCalculator.Money(net * rate / 100) };
    }
    private static async Task UpdateInvoiceEffects(TenantPlatformDbContext db, LeasingAcquisition a, CancellationToken ct)
    {
        var docs = await db.LeasingInvoices.Where(x => x.AccountId == a.AccountId && x.AcquisitionId == a.Id).Include(x => x.Lines).ToListAsync(ct);
        var credit = docs.Where(x => x.Kind == LeasingInvoiceKind.CreditNote && x.Status == LeasingInvoiceStatus.Approved).ToList();
        var reversed = docs.Where(x => x.Kind == LeasingInvoiceKind.Invoice && x.Status == LeasingInvoiceStatus.Reversed).SelectMany(x => x.Lines).Where(x => x.CreatedItem).ToList();
        a.CreditNetTotal = credit.Sum(x => x.Net); a.CreditVatTotal = credit.Sum(x => x.Vat);
        a.ReversedNetTotal = reversed.Sum(x => x.Net); a.ReversedVatTotal = reversed.Sum(x => x.Vat);
        a.ReleasedNetTotal = credit.Where(x => x.ReleasesLimit).Sum(x => x.Net) + a.ReversedNetTotal; a.ReleasedVatTotal = credit.Where(x => x.ReleasesLimit).Sum(x => x.Vat) + a.ReversedVatTotal;
        if (a.CreditNetTotal + a.ReversedNetTotal > a.NetTotal || a.CreditVatTotal + a.ReversedVatTotal > a.VatTotal) throw new LeasingValidationException("InvoiceOverCredit");
        a.Revision = Guid.NewGuid();
        if (a.FrameworkId.HasValue && a.Status == LeasingAcquisitionStatus.Registered)
        {
            var f = await db.LeasingFrameworks.SingleAsync(x => x.AccountId == a.AccountId && x.Id == a.FrameworkId, ct);
            var others = await db.LeasingAcquisitions.Where(x => x.AccountId == a.AccountId && x.FrameworkId == f.Id && x.Id != a.Id && x.Status == LeasingAcquisitionStatus.Registered).ToListAsync(ct);
            if (LeasingCalculator.Used(others.Append(a), f.IncludesVat) > f.Limit) throw new LeasingValidationException("LeasingLimitExceeded");
        }
    }
    public async Task ReverseInvoiceAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var invoice = await Invoices(db, account, user, admin).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (invoice.Status == LeasingInvoiceStatus.Reversed) return;
        CheckRevision(invoice.Revision, revision, true); Reason(reason, true);
        if (invoice.Status != LeasingInvoiceStatus.Approved) throw new LeasingValidationException("InvoiceImmutable");
        var a = await WithClassifications(Acquisitions(db, account, user, admin)).SingleAsync(x => x.Id == invoice.AcquisitionId, ct);
        var lineIds = invoice.Lines.Select(x => x.Id).ToArray(); var createdItems = invoice.Lines.Where(x => x.CreatedItem).Select(x => x.ItemId).ToArray();
        if (await db.LeasingInvoiceLines.AnyAsync(x => x.AccountId == account && x.InvoiceId != id && (lineIds.Contains(x.CreditedLineId ?? Guid.Empty) || createdItems.Contains(x.ItemId)) &&
            db.LeasingInvoices.Any(d => d.AccountId == account && d.Id == x.InvoiceId && d.Status == LeasingInvoiceStatus.Approved), ct)) throw new LeasingValidationException("InvoiceReverseDependencies");
        foreach (var line in invoice.Lines.Where(x => x.CreatedItem))
        { var item = a.Items.Single(x => x.Id == line.ItemId); var total = LeasingCalculator.Line(item); if (total.Net != line.Net || total.Vat != line.Vat) throw new LeasingValidationException("InvoiceReverseChangedItem"); }
        var before = Snapshot(a); invoice.Status = LeasingInvoiceStatus.Reversed; invoice.Revision = Guid.NewGuid();
        InvoiceHistory(db, invoice, user, "Reversed", reason, invoice.ApprovedJson!, invoice.ApprovedJson!);
        await db.SaveChangesAsync(ct); await UpdateInvoiceEffects(db, a, ct);
        History(db, account, null, a.Id, user, "InvoiceReversed", reason, before, Snapshot(a));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private void InvoiceHistory(TenantPlatformDbContext db, LeasingInvoice invoice, Guid? actor, string action, string reason, string before, string after) =>
        db.LeasingInvoiceHistory.Add(new() { Id = Guid.NewGuid(), AccountId = invoice.AccountId, InvoiceId = invoice.Id, ActorUserId = actor, RecordedUtc = clock.GetUtcNow(), Action = action, Reason = reason, BeforeJson = before, AfterJson = after });
}
