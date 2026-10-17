using System.Security.Cryptography;
using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing.Invoices;
public sealed class LeasingPurchaseInvoiceService(IAuthorizationService authorization,ILeasingPurchaseInvoiceData store,IAgreementDocumentStorage storage,LeasingAcquisitionWriter acquisitionWriter,TimeProvider clock)
{
    private async Task<LeasingReadScope> Actor(Guid account,CancellationToken ct)=>LeasingReader.Scope(await authorization.GetAccountActorScopeAsync(account,ct));
    private async Task<LeasingReadScope> Revalidate(LeasingReadScope actor,CancellationToken ct){var now=await Actor(actor.AccountId,ct);if(now.UserId!=actor.UserId)throw new UnauthorizedAccessException();return now;}
    private static bool Owns(LeasingReadScope actor,LeasingInvoicePurchase facts)=>actor.Administrator||facts.Acquisition?.OwnerUserId==actor.UserId||facts.Framework?.OwnerUserId==actor.UserId;
    private static void AuthorizePurchase(LeasingReadScope actor,LeasingInvoicePurchase facts,bool read)
    {
        if(facts.Acquisition is null||!Owns(actor,facts)&&!(read&&(actor.AcquisitionReader||actor.OrderApprover&&facts.HasOrderRealization)))throw new UnauthorizedAccessException();
    }
    private static async Task<LeasingInvoicePurchase> Purchase(ILeasingPurchaseInvoiceWork work,LeasingReadScope actor,Guid id,bool read,CancellationToken ct)
    {
        var facts=await work.ReadPurchaseAsync(id,ct);AuthorizePurchase(actor,facts,read);return facts;
    }
    private static async Task<LeasingInvoice> AuthorizedInvoice(ILeasingPurchaseInvoiceWork work,LeasingReadScope actor,bool read,CancellationToken ct)
    {
        var invoice=work.Invoice??throw new UnauthorizedAccessException();
        if(invoice.AcquisitionId is Guid id)await Purchase(work,actor,id,read,ct);
        else if(!actor.Administrator&&!(read&&actor.AcquisitionReader)&&invoice.UploadedByUserId!=actor.UserId)throw new UnauthorizedAccessException();
        return invoice;
    }
    private static async Task<LeasingInvoice> Editable(ILeasingPurchaseInvoiceWork work,LeasingReadScope actor,Guid revision,CancellationToken ct)
    {
        var invoice=await AuthorizedInvoice(work,actor,false,ct);CheckRevision(invoice.Revision,revision,true);if(invoice.Status!=LeasingInvoiceStatus.Review)throw new LeasingValidationException("InvoiceImmutable");return invoice;
    }
    private static async Task<LeasingOrder> Order(ILeasingPurchaseInvoiceWork work,LeasingReadScope actor,Guid id,CancellationToken ct)
    {
        var facts=await work.ReadOrderAsync(id,ct);if(facts.Order is null||!actor.Administrator&&facts.Order.OwnerUserId!=actor.UserId&&facts.Framework?.OwnerUserId!=actor.UserId)throw new UnauthorizedAccessException();return facts.Order;
    }
    private static async Task<LeasingFramework?> OwnedFramework(ILeasingPurchaseInvoiceWork work,LeasingReadScope actor,Guid id,CancellationToken ct)
    {
        var f=await work.ReadFrameworkAsync(id,ct);return f is not null&&(actor.Administrator||f.OwnerUserId==actor.UserId)?f:null;
    }
    private static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string Snapshot<T>(T value)=>JsonSerializer.Serialize(value);
    private static InvoiceReview ReadReview(LeasingInvoice invoice)=>JsonSerializer.Deserialize<InvoiceReview>(invoice.ReviewJson)??new();
    private static void CheckRevision(Guid actual,Guid supplied,bool existing){if(existing&&actual!=supplied)throw new LeasingValidationException("LeasingConcurrency");}
    private static void Reason(string reason,bool required){if(reason.Length>2000||required&&string.IsNullOrWhiteSpace(reason))throw new LeasingValidationException("LeasingReasonRequired");}
    private LeasingInvoiceHistory InvoiceHistory(LeasingInvoice invoice,Guid? actor,string action,string reason,string before,string after)=>new(){Id=Guid.NewGuid(),AccountId=invoice.AccountId,InvoiceId=invoice.Id,ActorUserId=actor,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow()};
    private LeasingHistory FinancialHistory(Guid account,Guid acquisition,Guid actor,string action,string reason,string before,string after)=>new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=acquisition,ActorUserId=actor,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow()};
    private static LeasingInvoice Public(LeasingInvoice invoice){invoice=Copy(invoice);invoice.StorageKey="";invoice.ProcessingToken=null;invoice.ProcessingError=null;return invoice;}
    public async Task<(List<LeasingInvoice> Documents,List<LeasingInvoiceLine> Lines)> PurchaseInvoicesAsync(Guid account,Guid acquisition,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,null,false,ct);var facts=await Purchase(work,actor,acquisition,true,ct);return(facts.Documents.OrderByDescending(x=>x.UploadedUtc).Select(Public).ToList(),facts.Lines);
    }
    public async Task<List<LeasingInvoice>> ListInvoicesAsync(Guid account,CancellationToken ct=default)=> (await store.ReadListAsync(await Actor(account,ct),ct)).Select(Public).ToList();
    public async Task<InvoiceDetails> GetInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,false,ct);actor=await Revalidate(actor,ct);var invoice=await AuthorizedInvoice(work,actor,true,ct);var review=ReadReview(invoice);
        var facts=invoice.AcquisitionId is Guid aid?await Purchase(work,actor,aid,true,ct):new LeasingInvoicePurchase(null,null,false,[],[]);
        var docs=facts.Documents.Where(x=>x.Status==LeasingInvoiceStatus.Approved).Select(x=>x.Id).ToHashSet();
        return new(Public(invoice),review,await work.ReadInterpretationsAsync(ct),await work.ReadHistoryAsync(ct),await work.ReadAcquisitionOptionsAsync(actor,ct),facts.Acquisition,facts.Lines.Where(x=>docs.Contains(x.InvoiceId)).ToList(),facts.Documents.Select(Public).ToList(),DuplicateWarnings(invoice,review.Data,await work.ReadApprovedAsync(ct)));
    }
    public async Task<AgreementDownload> DownloadInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,false,ct);actor=await Revalidate(actor,ct);var invoice=await AuthorizedInvoice(work,actor,true,ct);
        return new(await storage.OpenReadAsync(invoice.StorageKey,ct),invoice.FileName,invoice.MediaType);
    }
    private static void UploadAllowed(LeasingReadScope actor,Guid? acquisition,LeasingInvoiceUploadFacts facts)
    {
        if(acquisition.HasValue)
        {
            if(facts.Acquisition is null||facts.Acquisition.Status==LeasingAcquisitionStatus.Cancelled||!actor.Administrator&&facts.Acquisition.OwnerUserId!=actor.UserId&&facts.Framework?.OwnerUserId!=actor.UserId)throw new UnauthorizedAccessException();
        }
        else if(!actor.Administrator&&!facts.HasOwnedRoot)throw new UnauthorizedAccessException();
    }
    public async Task<Guid> UploadInvoiceAsync(Guid account,Guid? acquisition,string fileName,Stream content,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);UploadAllowed(actor,acquisition,await store.ReadUploadFactsAsync(account,actor.UserId,acquisition,ct));
        if(Path.GetExtension(fileName).ToLowerInvariant() is not (".xml" or ".pdf" or ".png" or ".jpg" or ".jpeg"))throw new AgreementFileException("AgreementInvalidFileType");
        var stored=await storage.StoreAsync(content,fileName,ct);
        try
        {
            await using var original=await storage.OpenReadAsync(stored.StorageKey,ct);var hash=Convert.ToHexString(await SHA256.HashDataAsync(original,ct));
            await using var work=await store.OpenAsync(account,null,true,ct);actor=await Revalidate(actor,ct);UploadAllowed(actor,acquisition,await store.ReadUploadFactsAsync(account,actor.UserId,acquisition,ct));
            var invoice=new LeasingInvoice{Id=Guid.NewGuid(),AccountId=account,AcquisitionId=acquisition,Category=LeasingInvoiceCategory.Equipment,UploadedByUserId=actor.UserId,UploadedUtc=clock.GetUtcNow(),FileName=stored.FileName,StorageKey=stored.StorageKey,MediaType=stored.MediaType,Size=stored.Size,FileHash=hash,Revision=Guid.NewGuid()};
            work.Invoice=invoice;await work.SaveInvoiceAsync(InvoiceHistory(invoice,actor.UserId,"Uploaded","","{}","{}"),ct);await work.CommitAsync(ct);return invoice.Id;
        }
        catch{await store.CleanupUncommittedAsync(account,stored.StorageKey);throw;}
    }
    private static List<string> DuplicateWarnings(LeasingInvoice invoice,InvoiceData data,IEnumerable<LeasingInvoice> others)
    {
        var warnings=new List<string>();var identity=Identity(data);var number=InvoiceNumber(data);
        if(others.Any(x=>x.FileHash==invoice.FileHash||identity!=""&&x.SupplierIdentity==identity&&x.Kind==data.Kind&&x.Number==number))warnings.Add("InvoiceCertainDuplicate");
        if(others.Any(x=>x.Kind==data.Kind&&(x.Number==number&&number!=""||x.InvoiceDate==data.Date&&x.Currency==data.Currency&&x.Net==data.Net&&x.Vat==data.Vat)))warnings.Add("InvoicePossibleDuplicate");return warnings;
    }
    public async Task SaveInvoiceReviewAsync(Guid account, Guid id, Guid revision, InvoiceReview review, CancellationToken ct = default)
    {
        review=Copy(review);
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,true,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await Editable(work,actor,revision,ct);
        if (review.AcquisitionId.HasValue)
        {
            review.AcquisitionRevision = (await Purchase(work,actor,review.AcquisitionId.Value,false,ct)).Acquisition is {Status:not LeasingAcquisitionStatus.Cancelled} selected?selected.Revision:throw new UnauthorizedAccessException();
        }
        else review.AcquisitionRevision = null;
        if (review.OrderDelivery != null)
        {
            var order = await Order(work,actor,review.OrderDelivery.OrderId,ct);
            if (review.Matches.Any(x => x.OrderLineId.HasValue && order.Lines.All(l => l.Id != x.OrderLineId))) throw new LeasingValidationException("LeasingOrderInvalid");
        }
        if (!review.AcquisitionId.HasValue && invoice.UploadedByUserId != user && !admin) throw new UnauthorizedAccessException();
        if (review.Data.Lines.Count > 500 || Snapshot(review).Length > 2_000_000 || review.ReviewReason.Length > 2000 || review.DuplicateOverrideReason.Length > 2000) throw new LeasingValidationException("InvoiceInvalidReview");
        var before = invoice.ReviewJson; invoice.ReviewJson = Snapshot(review); invoice.AcquisitionId = review.AcquisitionId;
        invoice.ReviewedByUserId = user; invoice.ReviewedUtc = clock.GetUtcNow(); invoice.Revision = Guid.NewGuid();
        await work.SaveInvoiceAsync(InvoiceHistory(invoice,user, "Reviewed", review.ReviewReason, before, invoice.ReviewJson),ct);
        await work.CommitAsync(ct);
    }
    public async Task RetryInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,true,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await Editable(work,actor,revision,ct);
        if (invoice.Processing is LeasingInvoiceProcessing.Processing or LeasingInvoiceProcessing.Uploaded) throw new LeasingValidationException("InvoiceProcessingBusy");
        invoice.Processing = LeasingInvoiceProcessing.Uploaded; invoice.ProcessingError = null; invoice.Revision = Guid.NewGuid();
        await work.SaveInvoiceAsync(InvoiceHistory(invoice,user, "Retry", "", "{}", "{}"),ct); await work.CommitAsync(ct);
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
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,true,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await Editable(work,actor,revision,ct); Reason(reason, true);
        invoice.Status = LeasingInvoiceStatus.Rejected; invoice.Revision = Guid.NewGuid();
        await work.SaveInvoiceAsync(InvoiceHistory(invoice,user, "Rejected", reason, invoice.ReviewJson, invoice.ReviewJson),ct);
        await work.CommitAsync(ct);
    }
    public static string Identity(InvoiceData data) => string.IsNullOrWhiteSpace(data.SupplierNumber) ? "" : new string(data.SupplierNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    public static string InvoiceNumber(InvoiceData data) => data.Number.Trim().ToUpperInvariant();
    public async Task<InvoiceApprovalPreview> PreviewInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,false,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await Editable(work,actor,revision,ct); var review = ReadReview(invoice);
        var a = review.AcquisitionId.HasValue ? (await Purchase(work,actor,review.AcquisitionId.Value,false,ct)).Acquisition! : review.NewAcquisition;
        var amounts = InvoiceCalculator.Calculate(review.Data); var f = a.FrameworkId.HasValue ? await OwnedFramework(work,actor,a.FrameworkId.Value,ct) : null;
        // Framework owner access is not required to view the limit of an already accessible acquisition.
        if (a.FrameworkId.HasValue && f == null && review.AcquisitionId.HasValue) f = await work.ReadFrameworkAsync(a.FrameworkId!.Value,ct)??throw new UnauthorizedAccessException();
        if (a.FrameworkId.HasValue && f == null && review.OrderDelivery != null && (await Order(work,actor,review.OrderDelivery.OrderId,ct)).FrameworkId==a.FrameworkId)
            f = await work.ReadFrameworkAsync(a.FrameworkId!.Value,ct)??throw new UnauthorizedAccessException();
        if (a.FrameworkId.HasValue && f == null) throw new UnauthorizedAccessException();
        var current = review.AcquisitionId.HasValue ? a.GrossTotal - a.CreditNetTotal - a.CreditVatTotal - a.ReversedNetTotal - a.ReversedVatTotal : 0;
        decimal changeNet = 0, changeVat = 0;
        for (int i = 0; i < review.Data.Lines.Count; i++)
            if (review.Data.Kind == LeasingInvoiceKind.CreditNote || review.Matches.SingleOrDefault(x => x.ReviewLineId == review.Data.Lines[i].ReviewId)?.ItemId == null)
            { changeNet += amounts.LineNet[i]; changeVat += amounts.LineVat[i]; }
        if (review.Data.Kind == LeasingInvoiceKind.CreditNote) { changeNet = -changeNet; changeVat = -changeVat; }
        var used = f == null ? 0 : (await work.ReadCapacityAsync(f.Id,ct)).Used;
        var delta = review.Data.Kind == LeasingInvoiceKind.CreditNote && f?.CreditNotesReleaseLimit != true ? 0 : changeNet + (f?.IncludesVat == true ? changeVat : 0);
        if (a.Status == LeasingAcquisitionStatus.Draft && review.Data.Kind == LeasingInvoiceKind.Invoice) delta += f?.IncludesVat == true ? a.GrossTotal : a.NetTotal;
        decimal reserved = f == null ? 0 : (await work.ReadCapacityAsync(f.Id,ct)).Reserved, released = 0;
        if (review.OrderDelivery != null)
        {
            var o = await Order(work,actor,review.OrderDelivery.OrderId,ct);
            CheckRevision(o.Revision, review.OrderDelivery.Revision, true);
            var parts = review.Matches.Where(x => x.OrderLineId.HasValue && !x.ItemId.HasValue).Select(x => new OrderDeliveryLine { OrderLineId = x.OrderLineId!.Value, Scope = x.OrderScope }).ToList();
            released = LeasingOrderPreviews.PreviewOrderRelease(o, new() { Lines = parts }, f?.IncludesVat == true);
        }
        return new(current, changeNet + changeVat, current + changeNet + changeVat, used, used + (f == null ? 0 : delta), f?.Limit, f?.CreditNotesReleaseLimit, reserved, released);
    }
    public async Task<Guid> ApproveInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,true,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await AuthorizedInvoice(work,actor,false,ct);
        if (invoice.Status == LeasingInvoiceStatus.Approved) return invoice.AcquisitionId!.Value; // idempotent retry
        CheckRevision(invoice.Revision, revision, true);
        if (invoice.Status != LeasingInvoiceStatus.Review || invoice.Processing is LeasingInvoiceProcessing.Uploaded or LeasingInvoiceProcessing.Processing) throw new LeasingValidationException("InvoiceImmutable");
        var review = ReadReview(invoice); var data = review.Data; var amounts = InvoiceCalculator.Calculate(data);
        if (data.Kind == LeasingInvoiceKind.CreditNote && review.OrderDelivery != null) throw new LeasingValidationException("LeasingOrderAlreadyDocumented");
        if (!review.ContentConfirmed || !Enum.IsDefined(data.Kind) || string.IsNullOrWhiteSpace(data.SupplierName) || data.SupplierName.Length > 200 || data.SupplierNumber.Length > 200 ||
            string.IsNullOrWhiteSpace(data.Number) || data.Number.Length > 100 || data.Date is null || data.Date == DateOnly.MinValue || !AgreementPeriodCalculator.Currencies.Contains(data.Currency)) throw new LeasingValidationException("InvoiceReviewRequired");
        if (amounts.Errors.Count > 0) throw new LeasingValidationException(amounts.Errors[0]);
        var duplicates = DuplicateWarnings(invoice,data,await work.ReadApprovedAsync(ct));
        if (duplicates.Contains("InvoiceCertainDuplicate")) throw new LeasingValidationException("InvoiceCertainDuplicate");
        if (duplicates.Contains("InvoicePossibleDuplicate") && string.IsNullOrWhiteSpace(review.DuplicateOverrideReason)) throw new LeasingValidationException("InvoiceDuplicateReason");
        if (review.Matches.Select(x => x.ReviewLineId).Distinct().Count() != review.Matches.Count || data.Lines.Select(x => x.ReviewId).Distinct().Count() != data.Lines.Count ||
            data.Lines.Any(x => review.Matches.All(m => m.ReviewLineId != x.ReviewId))) throw new LeasingValidationException("InvoiceMatchRequired");
        LeasingAcquisition a;
        if (review.AcquisitionId.HasValue) a = (await Purchase(work,actor,review.AcquisitionId.Value,false,ct)).Acquisition!;
        else
        {
            if (data.Kind == LeasingInvoiceKind.CreditNote || !review.PurchaseDateConfirmed) throw new LeasingValidationException("InvoicePurchaseDateConfirm");
            a = JsonSerializer.Deserialize<LeasingAcquisition>(Snapshot(review.NewAcquisition))!; a.Items = []; a.Id = Guid.Empty;
        }
        if (review.AcquisitionId.HasValue && review.AcquisitionRevision != a.Revision) throw new LeasingValidationException("LeasingConcurrency");
        if (a.Status == LeasingAcquisitionStatus.Cancelled || a.Currency != data.Currency) throw new LeasingValidationException("InvoicePurchaseConflict");
        var party = await work.ReadOrganizationAsync(a.SupplierOrganizationId,ct) ?? throw new LeasingValidationException("LeasingInvalidParty");
        if (!string.IsNullOrWhiteSpace(party.OrganizationNumber) && Identity(data) != new string(party.OrganizationNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()) throw new LeasingValidationException("InvoiceSupplierMismatch");
        var financialBefore = review.AcquisitionId.HasValue ? Snapshot(a) : "{}";
        var input = JsonSerializer.Deserialize<LeasingAcquisition>(Snapshot(a))!; input.Status = LeasingAcquisitionStatus.Registered;
        var purchaseFacts=a.Id==Guid.Empty?new LeasingInvoicePurchase(null,null,false,[],[]):await work.ReadPurchaseAsync(a.Id,ct);
        var docs=purchaseFacts.Documents.Where(x=>x.Status==LeasingInvoiceStatus.Approved).ToDictionary(x=>x.Id);
        var originals=purchaseFacts.Lines.Where(x=>docs.ContainsKey(x.InvoiceId)).ToList();
        var classifications = new Dictionary<int, LeasingClassificationInput>(); var targets = new List<(InvoiceDataLine Source, Guid? Target, int Position, Guid? Credit)>();
        var pending = new List<LeasingInvoiceLine>();
        var reversedItems=purchaseFacts.Lines.Where(x=>x.CreatedItem&&purchaseFacts.Documents.Any(d=>d.Id==x.InvoiceId&&d.Status==LeasingInvoiceStatus.Reversed)).Select(x=>x.ItemId).ToList();
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
            if (review.OrderDelivery != null)
            {
                review.OrderDelivery.Lines = [];
                foreach (var target in targets)
                {
                    var match = review.Matches.Single(x => x.ReviewLineId == target.Source.ReviewId);
                    if (!match.OrderLineId.HasValue) continue;
                    if (target.Target.HasValue) throw new LeasingValidationException("LeasingOrderAlreadyDocumented");
                    review.OrderDelivery.Lines.Add(new() { OrderLineId = match.OrderLineId.Value, ItemPosition = target.Position, Scope = match.OrderScope });
                }
            }
            await using var acquisitionWork = work.AcquisitionWork;
            var aid = await acquisitionWriter.SaveForInvoiceAsync(acquisitionWork, id, review.AcquisitionId, input, "Invoice approval " + id, classifications, review.OrderDelivery, ct);
            a = (await work.ReadPurchaseAsync(aid,ct)).Acquisition!;
        }
        else
        {
            if (a.Status != LeasingAcquisitionStatus.Registered || a.Items.Any(x => !LeasingAllocationCalculator.Calculate(x).Complete)) throw new LeasingValidationException("LeasingInvalidAllocation");
            if (a.FrameworkId.HasValue)
            {
                var f = await work.ReadFrameworkAsync(a.FrameworkId.Value,ct)??throw new UnauthorizedAccessException();
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
        await work.SaveInvoiceAsync(InvoiceHistory(invoice,user, "Approved", review.DuplicateOverrideReason, invoice.ReviewJson, invoice.ApprovedJson),ct);
        await UpdateInvoiceEffects(work,a,ct);
        await work.SaveFinancialHistoryAsync(FinancialHistory(account,a.Id,user, "InvoiceApproved", id.ToString(), financialBefore, Snapshot(a)),ct);
        await work.CommitAsync(ct); return a.Id;
    }
    public static LeasingItem ImportedItem(InvoiceDataLine line, decimal net, decimal vat)
    {
        var price = line.DocumentAdjustment ? 0 : decimal.Round((line.Price ?? 0) / line.BaseQuantity, 4, MidpointRounding.AwayFromZero);
        var qty = line.Quantity ?? 1; var rate = line.VatPercent ?? 0;
        return new() { Description = line.Description, ItemNumber = line.ItemNumber, Quantity = qty, UnitPrice = price, VatPercent = rate,
            InvoiceNetAdjustment = net - InvoiceCalculator.Money(qty * price), InvoiceVatAdjustment = vat - InvoiceCalculator.Money(net * rate / 100) };
    }
    private static async Task UpdateInvoiceEffects(ILeasingPurchaseInvoiceWork work,LeasingAcquisition a,CancellationToken ct)
    {
        var docs=(await work.ReadPurchaseAsync(a.Id,ct));
        var credit=docs.Documents.Where(x=>x.Kind==LeasingInvoiceKind.CreditNote&&x.Status==LeasingInvoiceStatus.Approved).ToList();
        var reversed=docs.Lines.Where(x=>x.CreatedItem&&docs.Documents.Any(d=>d.Id==x.InvoiceId&&d.Kind==LeasingInvoiceKind.Invoice&&d.Status==LeasingInvoiceStatus.Reversed)).ToList();
        a.CreditNetTotal=credit.Sum(x=>x.Net);a.CreditVatTotal=credit.Sum(x=>x.Vat);a.ReversedNetTotal=reversed.Sum(x=>x.Net);a.ReversedVatTotal=reversed.Sum(x=>x.Vat);
        a.ReleasedNetTotal=credit.Where(x=>x.ReleasesLimit).Sum(x=>x.Net)+a.ReversedNetTotal;a.ReleasedVatTotal=credit.Where(x=>x.ReleasesLimit).Sum(x=>x.Vat)+a.ReversedVatTotal;
        if(a.CreditNetTotal+a.ReversedNetTotal>a.NetTotal||a.CreditVatTotal+a.ReversedVatTotal>a.VatTotal)throw new LeasingValidationException("InvoiceOverCredit");
        a.Revision=Guid.NewGuid();
        if(a.FrameworkId.HasValue&&a.Status==LeasingAcquisitionStatus.Registered){var capacity=await work.ReadCapacityAsync(a.FrameworkId.Value,ct);if(capacity.Used+capacity.Reserved>capacity.Limit)throw new LeasingValidationException("LeasingLimitExceeded");}
    }
    public async Task ReverseInvoiceAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,true,ct);actor=await Revalidate(actor,ct);var user=actor.UserId;var admin=actor.Administrator;
        var invoice = await AuthorizedInvoice(work,actor,false,ct);
        if (invoice.Status == LeasingInvoiceStatus.Reversed) return;
        CheckRevision(invoice.Revision, revision, true); Reason(reason, true);
        if (invoice.Status != LeasingInvoiceStatus.Approved) throw new LeasingValidationException("InvoiceImmutable");
        var a = (await Purchase(work,actor,invoice.AcquisitionId!.Value,false,ct)).Acquisition!;
        var lineIds = invoice.Lines.Select(x => x.Id).ToArray(); var createdItems = invoice.Lines.Where(x => x.CreatedItem).Select(x => x.ItemId).ToArray();
        if(await work.HasDependenciesAsync(id,lineIds,createdItems,ct)) throw new LeasingValidationException("InvoiceReverseDependencies");
        foreach (var line in invoice.Lines.Where(x => x.CreatedItem))
        { var item = a.Items.Single(x => x.Id == line.ItemId); var total = LeasingCalculator.Line(item); if (total.Net != line.Net || total.Vat != line.Vat) throw new LeasingValidationException("InvoiceReverseChangedItem"); }
        var before = Snapshot(a);
        await using var acquisitionWork = work.AcquisitionWork;
        await acquisitionWriter.ReverseOrderPartsAsync(acquisitionWork, a.Id, createdItems, ct);
        invoice.Status = LeasingInvoiceStatus.Reversed; invoice.Revision = Guid.NewGuid();
        await work.SaveInvoiceAsync(InvoiceHistory(invoice,user, "Reversed", reason, invoice.ApprovedJson!, invoice.ApprovedJson!),ct);
        await UpdateInvoiceEffects(work,a,ct);
        await work.SaveFinancialHistoryAsync(FinancialHistory(account,a.Id,user, "InvoiceReversed", reason, before, Snapshot(a)),ct);
        await work.CommitAsync(ct);
    }
}
