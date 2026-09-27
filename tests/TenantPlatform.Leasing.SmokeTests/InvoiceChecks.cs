using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Leasing;
using TenantPlatform.Web.Services.Leasing.Invoices;

static class InvoiceChecks
{
    public static async Task Run(IDbContextFactory<TenantPlatformDbContext> factory, Guid account, Guid adminId, Guid ownerId, Guid outsiderId, Guid party, Guid foreignAccount)
    {
        ParserChecks();
        var directory = Path.Combine(Path.GetTempPath(), "leasing-invoices-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalAgreementDocumentStorage(Options.Create(new AgreementDocumentStorageOptions { RootPath = directory, MaxFileSizeBytes = 1024 * 1024 }));
        try
        {
            LeasingService Service(Guid user) { var context = new Context(user, account); return new(factory, context, new TenantAuthorizationService(factory, context), storage, new Clock(), NullLogger<LeasingService>.Instance); }
            var admin = Service(adminId); var owner = Service(ownerId); var outsider = Service(outsiderId);
            Guid dimension, value;
            await using (var db = factory.CreateDbContext()) { dimension = await db.Dimensions.Where(x => x.AccountId == account && x.Code == "DEP").Select(x => x.Id).SingleAsync(); value = await db.DimensionValues.Where(x => x.AccountId == account && x.DimensionId == dimension && x.Code == "TECH").Select(x => x.Id).SingleAsync(); }
            LeasingClassificationInput Classification() => new() { Common = [new(dimension, value)] };
            var http = new FakeHttp();
            var interpreter = new InvoiceDocumentInterpreter(new HttpClient(http), storage, Options.Create(new LeasingInvoiceInterpretationOptions { PdfTextExecutable = "" }), Options.Create(new AgreementAnalysisOptions()));
            var processor = new LeasingInvoiceProcessor(factory, interpreter, new Clock());
            var frame = new LeasingFramework { Name = "Invoice test frame", Number = "INV-FRAME", OwnerUserId = adminId, FinanceOrganizationId = party, AcquisitionFrom = new(2027,1,1), AcquisitionTo = new(2027,12,31), Limit = 100000 };
            var frameId = await admin.SaveFrameworkAsync(account, null, frame, "");
            var purchase = await admin.NewAcquisitionAsync(account, frameId); purchase.Name = "Manual purchase"; purchase.Reference = "PO"; purchase.OwnerUserId = ownerId;
            purchase.SupplierOrganizationId = party; purchase.PurchaseDate = new(2027,2,1); purchase.InvoiceNumber = "Legacy reference"; purchase.InvoiceDate = new(2027,2,1); purchase.Status = LeasingAcquisitionStatus.Registered; purchase.FinancedAmount = 80000;
            purchase.Items = [new() { Description = "Equipment", Quantity = 1, UnitPrice = 100000, VatPercent = 25 }];
            var aid = await admin.SaveAcquisitionAsync(account, null, purchase, "", classifications: new Dictionary<int, LeasingClassificationInput> { [0] = Classification() });
            async Task<LeasingAcquisition> Read() => (await owner.GetAsync(account, aid, false)).Acquisition!;
            var original = await Read(); var itemId = original.Items.Single().Id;
            async Task<decimal> Used() => (await admin.GetAsync(account, frameId, true)).Used;
            async Task<Guid> Import(string number, decimal quantity, decimal price, bool credit = false, Guid? target = null)
            {
                var usedBeforeUpload = await Used();
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(Ubl(number, quantity, price, credit)));
                var id = await admin.UploadInvoiceAsync(account, target ?? aid, number + ".xml", input);
                Check(await Used() == usedBeforeUpload && (await admin.GetInvoiceAsync(account, id)).Invoice.Status == LeasingInvoiceStatus.Review, "upload has no approved economic effect");
                await processor.ProcessOneAsync();
                Check((await admin.GetInvoiceAsync(account, id)).Invoice.Processing == LeasingInvoiceProcessing.Ready, "UBL processing reaches ready-for-review independently of approval");
                return id;
            }
            async Task<InvoiceDetails> Save(Guid id, Action<InvoiceReview> modify)
            { var d = await owner.GetInvoiceAsync(account, id); var r = d.Review; r.ContentConfirmed = true; modify(r); await owner.SaveInvoiceReviewAsync(account, id, d.Invoice.Revision, r); return await owner.GetInvoiceAsync(account, id); }
            async Task Approve(Guid id) { var d = await owner.GetInvoiceAsync(account, id); await owner.ApproveInvoiceAsync(account, id, d.Invoice.Revision); }
            var first = await Import("INV-40000", .4m, 100000);
            var firstDetail = await Save(first, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]);
            Check((await owner.PreviewInvoiceAsync(account, first, firstDetail.Invoice.Revision)).Change == 0, "matching preview shows no increase in purchase value");
            await Approve(first); await Approve(first);
            Check(await Used() == 100000 && (await Read()).NetTotal == 100000, "40,000 partial invoice matches manual 100,000 purchase without double counting; approval retry is idempotent");
            var second = await Import("INV-60000", .6m, 100000);
            await Save(second, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]); await Approve(second);
            var summary = await owner.PurchaseInvoicesAsync(account, aid);
            Check(summary.Lines.Sum(x => x.Net) == 100000 && summary.Lines.Sum(x => x.Quantity) == 1 && await Used() == 100000, "multiple partial deliveries document exactly the original quantity/value");
            var after = await Read(); Check(after.PurchaseDate == original.PurchaseDate && after.EndDate == original.EndDate && after.FinancedAmount == original.FinancedAmount && after.Items.Single().DimensionSelections.Single().ValueId == value, "matching preserves purchase date, period, financing and dimensions");
            var over = await Import("INV-EXCESS", .1m, 100000); await Save(over, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]); await Reject(() => Approve(over), "InvoiceOverDocumented");
            var originalLine = (await owner.GetInvoiceAsync(account, first)).Invoice.Lines.Single();
            var credit = await Import("CN-20000", .2m, 100000, true);
            await Save(credit, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId, CreditedLineId = originalLine.Id }]);
            await Reject(() => Approve(credit), "InvoiceCreditRuleRequired");
            var editFrame = (await admin.GetAsync(account, frameId, true)).Framework!; editFrame.CreditNotesReleaseLimit = false; await admin.SaveFrameworkAsync(account, frameId, editFrame, "Credits do not release limit");
            await Approve(credit); Check(await Used() == 100000 && (await Read()).CreditNetTotal == 20000, "credit reduces net purchase value while explicit false policy retains capacity usage");
            editFrame = (await admin.GetAsync(account, frameId, true)).Framework!; editFrame.CreditNotesReleaseLimit = true;
            await Reject(() => admin.SaveFrameworkAsync(account, frameId, editFrame, "Change policy"), "InvoiceCreditRuleLocked");
            var c = await owner.GetInvoiceAsync(account, credit); await owner.ReverseInvoiceAsync(account, credit, c.Invoice.Revision, "Correct policy before reimport");
            editFrame = (await admin.GetAsync(account, frameId, true)).Framework!; editFrame.CreditNotesReleaseLimit = true; await admin.SaveFrameworkAsync(account, frameId, editFrame, "Credits release limit");
            var credit2 = await Import("CN-20000", .2m, 100000, true); await Save(credit2, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId, CreditedLineId = originalLine.Id }]); await Approve(credit2);
            Check(await Used() == 80000 && (await Read()).CreditNetTotal == 20000, "credit sign applied exactly once and explicit true policy releases capacity");
            var excessiveCredit = await Import("CN-OVER", .3m, 100000, true); await Save(excessiveCredit, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId, CreditedLineId = originalLine.Id }]); await Reject(() => Approve(excessiveCredit), "InvoiceOverCredit");
            var dfirst = await owner.GetInvoiceAsync(account, first); await Reject(() => owner.ReverseInvoiceAsync(account, first, dfirst.Invoice.Revision, "Has dependent credit"), "InvoiceReverseDependencies");
            var duplicate = await Import("INV-40000", .4m, 100000); await Save(duplicate, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]); await Reject(() => Approve(duplicate), "InvoiceCertainDuplicate");
            // PDF without configured interpreter remains manually reviewable; same identity/number as XML is a certain duplicate.
            var pdf = await admin.UploadInvoiceAsync(account, aid, "supplier.pdf", new MemoryStream(Pdf())); await processor.ProcessOneAsync();
            var pdfDetails = await owner.GetInvoiceAsync(account, pdf);
            Check(pdfDetails.Interpretations.Single().WarningsJson.Contains("InvoiceInterpreterNotConfigured") && http.Calls == 0, "missing PDF configuration performs no external request and exposes manual review");
            await Save(pdf, r => { r.Data = JsonSerializer.Deserialize<InvoiceData>(dfirst.Invoice.ApprovedJson is null ? "{}" : JsonSerializer.Serialize(dfirst.Review.Data))!; r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]; });
            await Reject(() => Approve(pdf), "InvoiceCertainDuplicate");
            pdfDetails = await owner.GetInvoiceAsync(account, pdf); var savedJson = pdfDetails.Invoice.ReviewJson;
            await owner.RetryInvoiceAsync(account, pdf, pdfDetails.Invoice.Revision); await processor.ProcessOneAsync();
            pdfDetails = await owner.GetInvoiceAsync(account, pdf); Check(pdfDetails.Invoice.ReviewJson == savedJson && pdfDetails.Interpretations.Count == 2, "retry appends interpretation without overwriting manual corrections");
            await owner.UseInterpretationAsync(account, pdf, pdfDetails.Invoice.Revision, pdfDetails.Interpretations[0].Id); Check(!(await owner.GetInvoiceAsync(account, pdf)).Review.ContentConfirmed, "explicit replacement requires fresh review confirmation");
            var beforeReject = await Used(); pdfDetails = await owner.GetInvoiceAsync(account, pdf); await owner.RejectInvoiceAsync(account, pdf, pdfDetails.Invoice.Revision, "Not relevant"); Check(await Used() == beforeReject, "reject has no financial effect");
            // Two concurrent approvals for new acquisitions compete for the released 20,000.
            async Task<Guid> NewInvoice(string number)
            {
                using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(Ubl(number, 1, 15000)));
                var id = await admin.UploadInvoiceAsync(account, null, number + ".xml", bytes); await processor.ProcessOneAsync();
                var detail = await admin.GetInvoiceAsync(account, id); var r = detail.Review; r.ContentConfirmed = true; r.PurchaseDateConfirmed = true; r.DuplicateOverrideReason = "Separate equipment deliveries";
                r.NewAcquisition = await admin.NewAcquisitionAsync(account, frameId); r.NewAcquisition.Name = number; r.NewAcquisition.Reference = number; r.NewAcquisition.SupplierOrganizationId = party; r.NewAcquisition.OwnerUserId = ownerId; r.NewAcquisition.PurchaseDate = new(2027,3,1); r.NewAcquisition.FinancedAmount = 10000;
                r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, Classification = Classification() }];
                await admin.SaveInvoiceReviewAsync(account, id, detail.Invoice.Revision, r); return id;
            }
            var one = await NewInvoice("NEW-A"); var two = await NewInvoice("NEW-B");
            var candidates = new[] { await admin.GetInvoiceAsync(account, one), await admin.GetInvoiceAsync(account, two) };
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var approvals = candidates.Select(async d => { await gate.Task; try { await admin.ApproveInvoiceAsync(account, d.Invoice.Id, d.Invoice.Revision); return d.Invoice.Id; } catch (LeasingValidationException ex) when (ex.Message == "LeasingLimitExceeded") { return Guid.Empty; } }).ToArray();
            gate.SetResult(); var approvedIds = await Task.WhenAll(approvals); var winner = approvedIds.Single(x => x != Guid.Empty);
            Check(approvedIds.Count(x => x != Guid.Empty) == 1 && await Used() == 95000, "concurrent approvals serialize and only one fits the framework");
            c = await owner.GetInvoiceAsync(account, credit2); await Reject(() => owner.ReverseInvoiceAsync(account, credit2, c.Invoice.Revision, "Capacity is already used"), "LeasingLimitExceeded");
            Check((await owner.GetInvoiceAsync(account, credit2)).Invoice.Status == LeasingInvoiceStatus.Approved, "failed reversal rolls back status and credit effect atomically");
            var winnerDetails = await admin.GetInvoiceAsync(account, winner); await admin.ReverseInvoiceAsync(account, winner, winnerDetails.Invoice.Revision, "Correct imported purchase");
            await admin.ReverseInvoiceAsync(account, winner, winnerDetails.Invoice.Revision, "Idempotent reversal retry");
            Check(await Used() == 80000 && (await admin.GetInvoiceAsync(account, winner)).Invoice.Lines.Count == 1, "new-line reversal releases only its created value and preserves source links");
            c = await owner.GetInvoiceAsync(account, credit2); await owner.ReverseInvoiceAsync(account, credit2, c.Invoice.Revision, "Reverse credit"); Check(await Used() == 100000 && (await Read()).CreditNetTotal == 0, "credit reversal restores original financial position");
            await Deny(() => outsider.GetInvoiceAsync(account, first)); await Deny(() => owner.GetInvoiceAsync(foreignAccount, first)); await Deny(() => outsider.DownloadInvoiceAsync(account, first));
            var invalidLink = await Import("BAD-LINK", 1, 1); await Save(invalidLink, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = Guid.NewGuid() }]); await Reject(() => Approve(invalidLink), "InvoiceMatchRequired");
            var fullCredit = await Import("CN-FULL", 1, 100000, true);
            var secondLine = (await owner.GetInvoiceAsync(account, second)).Invoice.Lines.Single();
            await Save(fullCredit, r =>
            {
                var firstPart = r.Data.Lines[0]; firstPart.Quantity = .4m; firstPart.Net = 40000; firstPart.SourceId = "1-a";
                var lastPart = JsonSerializer.Deserialize<InvoiceDataLine>(JsonSerializer.Serialize(firstPart))!; lastPart.ReviewId = Guid.NewGuid(); lastPart.SourceId = "1-b"; lastPart.Quantity = .6m; lastPart.Net = 60000; r.Data.Lines.Add(lastPart);
                r.Matches = [new() { ReviewLineId = firstPart.ReviewId, ItemId = itemId, CreditedLineId = originalLine.Id }, new() { ReviewLineId = lastPart.ReviewId, ItemId = itemId, CreditedLineId = secondLine.Id }];
            });
            await Approve(fullCredit); Check(await Used() == 0 && (await Read()).CreditNetTotal == 100000 && (await Read()).FinancedAmount == original.FinancedAmount, "full credit across two deliveries releases the exact remaining value without changing financing");
            var full = await owner.GetInvoiceAsync(account, fullCredit); await owner.ReverseInvoiceAsync(account, fullCredit, full.Invoice.Revision, "Undo full credit");
            // A changed acquisition after review requires refreshed confirmation, rather than approving a stale effect.
            var staleInvoice = await Import("STALE", .1m, 100000);
            await Save(staleInvoice, r => r.Matches = [new() { ReviewLineId = r.Data.Lines[0].ReviewId, ItemId = itemId }]);
            var changed = await Read(); changed.Notes = "Changed after invoice review"; await owner.SaveAcquisitionAsync(account, aid, changed, "Concurrent note change");
            await Reject(() => Approve(staleInvoice), "LeasingConcurrency");
            await Reject(async () => await owner.SaveInvoiceReviewAsync(account, first, (await owner.GetInvoiceAsync(account, first)).Invoice.Revision, firstDetail.Review), "InvoiceImmutable");
            // Persist explicit document-level adjustments rather than burying them in unit prices.
            using var adjustedBytes = new MemoryStream(Encoding.UTF8.GetBytes(Ubl("ADJUSTED", 1, 100)));
            var adjustedId = await admin.UploadInvoiceAsync(account, null, "adjusted.xml", adjustedBytes); await processor.ProcessOneAsync();
            var adjusted = await admin.GetInvoiceAsync(account, adjustedId); var adjustedReview = adjusted.Review;
            adjustedReview.ContentConfirmed = true; adjustedReview.PurchaseDateConfirmed = true;
            adjustedReview.NewAcquisition = await admin.NewAcquisitionAsync(account, null);
            adjustedReview.NewAcquisition.FinancedAmount = 90;
            adjustedReview.NewAcquisition.Name = "Explicit adjustments"; adjustedReview.NewAcquisition.Reference = "ADJUSTED";
            adjustedReview.NewAcquisition.FinanceOrganizationId = party; adjustedReview.NewAcquisition.SupplierOrganizationId = party;
            adjustedReview.Data.Lines.Add(new() { SourceId = "discount", Description = "Document discount", DocumentAdjustment = true, IsAllowance = true, Quantity = 1, Price = 10, Net = -10, VatPercent = 25, VatCategory = "S" });
            adjustedReview.Data.Net = 90; adjustedReview.Data.Vat = 22.5m; adjustedReview.Data.Gross = 112.5m; adjustedReview.Data.Payable = 112.5m;
            adjustedReview.Data.Taxes = [];
            adjustedReview.Matches = adjustedReview.Data.Lines.Select(x => new InvoiceLineMatch { ReviewLineId = x.ReviewId, Classification = Classification() }).ToList();
            await admin.SaveInvoiceReviewAsync(account, adjustedId, adjusted.Invoice.Revision, adjustedReview);
            adjusted = await admin.GetInvoiceAsync(account, adjustedId);
            var adjustedPurchaseId = await admin.ApproveInvoiceAsync(account, adjustedId, adjusted.Invoice.Revision);
            var adjustedPurchase = (await admin.GetAsync(account, adjustedPurchaseId, false)).Acquisition!;
            Check(adjustedPurchase.NetTotal == 90 && adjustedPurchase.VatTotal == 22.5m && adjustedPurchase.Items.Count == 2 && adjustedPurchase.Items.Single(x => x.Description == "Document discount").InvoiceNetAdjustment == -10,
                "document discount persists as an explicit classified line and reconciles purchase and VAT totals");
            await InvoiceComponentChecks.Run(admin, account, adminId, invalidLink, first);
            await AdapterChecks(storage, factory, account, admin, aid);
            await using (var db = factory.CreateDbContext()) Check(await db.LeasingInvoiceHistory.AnyAsync(x => x.AccountId == account && x.InvoiceId == first && x.Action == "Approved" && x.ActorUserId == ownerId), "approval audit includes actor and preserved reviewed data");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void ParserChecks()
    {
        using var invoice = new MemoryStream(Encoding.UTF8.GetBytes(Ubl("PARSE", 2, 50)));
        var data = UblInvoiceParser.Parse(invoice); Check(data.Lines[0].SourceId == "1" && data.Lines[0].Unit == "EA" && InvoiceCalculator.Calculate(data).Errors.Count == 0, "UBL Invoice 2.1 parses quantities, unit, price, VAT and totals");
        using var credit = new MemoryStream(Encoding.UTF8.GetBytes(Ubl("CREDIT", 2, 50, true)));
        var c = UblInvoiceParser.Parse(credit); Check(c.Kind == LeasingInvoiceKind.CreditNote && c.Net == 100 && c.Lines[0].Quantity == 2, "UBL CreditNote keeps positive magnitudes, not double-negative credits");
        var multi = new InvoiceData { Net = 220, Vat = 31.75m, Gross = 251.75m, Prepaid = 50, Rounding = .25m, Payable = 202,
            Lines = [new() { Description = "Price base", SourceId = "1", Quantity = 4, Price = 60, BaseQuantity = 2, Allowance = 5, Charge = 2, Net = 117, VatPercent = 25, VatCategory = "S" }, new() { Description = "Zero rated", SourceId = "2", Quantity = 1, Price = 100, Net = 100, VatPercent = 0, VatCategory = "Z" }, new() { Description = "Freight", SourceId = "3", Quantity = 1, Price = 10, Net = 10, VatPercent = 25, VatCategory = "S", DocumentAdjustment = true }, new() { Description = "Discount", SourceId = "4", Quantity = 1, Price = 7, Net = -7, VatPercent = 0, VatCategory = "Z", DocumentAdjustment = true, IsAllowance = true }] };
        var calculated = InvoiceCalculator.Calculate(multi); Check(calculated.Errors.Count == 0 && calculated.Taxes.Count == 2 && calculated.Net == 220 && calculated.Gross == 251.75m, "line/header allowances, charges, price bases, multiple VAT groups and prepayment reconcile without reducing purchase value");
        multi.Lines.Add(new() { Description = "Exempt", Quantity = 1, Price = 0, Net = 0, VatPercent = 0, VatCategory = "E" }); Check(InvoiceCalculator.Calculate(multi).Taxes.Count == 3, "zero VAT categories remain distinct");
        multi.Rounding = 2; Check(InvoiceCalculator.Calculate(multi).Errors.Contains("InvoicePayableMismatch"), "unexplained/large rounding blocks approval");
        try { using var malicious = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE Invoice [<!ENTITY secret SYSTEM 'file:///etc/passwd'>]><Invoice>&secret;</Invoice>")); UblInvoiceParser.Parse(malicious); throw new Exception("XXE accepted"); } catch (XmlException) { Check(true, "DTD/external entities are rejected"); }
        try { using var unsupported = new MemoryStream(Encoding.UTF8.GetBytes(Ubl("VERSION",1,1).Replace("<cbc:UBLVersionID>2.1", "<cbc:UBLVersionID>2.0"))); UblInvoiceParser.Parse(unsupported); throw new Exception("Unsupported accepted"); } catch (LeasingValidationException ex) when (ex.Message == "InvoiceUnsupportedXml") { Check(true, "unsupported UBL version rejected explicitly"); }
    }
    private static async Task AdapterChecks(IAgreementDocumentStorage storage, IDbContextFactory<TenantPlatformDbContext> factory, Guid account, LeasingService admin, Guid aid)
    {
        var transport = new FakeHttp { Result = new InvoiceData { SupplierName = "Document supplier", Number = "PDF-EXTRACT", Lines = [new() { Description = "Page two", Page = 2, Quantity = null }] } };
        var configured = new InvoiceDocumentInterpreter(new HttpClient(transport), storage, Options.Create(new LeasingInvoiceInterpretationOptions { Enabled = true, PdfTextExecutable = "" }), Options.Create(new AgreementAnalysisOptions { ApiKey = "test-only", Model = "configured-test-model" }));
        var pdf = await admin.UploadInvoiceAsync(account, aid, "two-pages.pdf", new MemoryStream(Pdf()));
        var processor = new LeasingInvoiceProcessor(factory, configured, new Clock()); await processor.ProcessOneAsync();
        var details = await admin.GetInvoiceAsync(account, pdf);
        Check(details.Review.Data.Lines.Single().Page == 2 && details.Review.Data.Lines[0].Quantity == null && transport.LastRequest.Contains("input_file") && transport.LastRequest.Contains("\"store\":false") && transport.LastRequest.Contains("json_schema") && transport.LastRequest.Contains("additionalProperties"), "configured PDF adapter sends complete original and preserves page references and missing values");
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jC1kAAAAASUVORK5CYII=");
        var img = await admin.UploadInvoiceAsync(account, aid, "scan.png", new MemoryStream(image)); await processor.ProcessOneAsync();
        Check(transport.LastRequest.Contains("input_image") && (await admin.GetInvoiceAsync(account, img)).Invoice.Processing == LeasingInvoiceProcessing.Ready, "configured image adapter contract uses image input");
        var broken = await admin.UploadInvoiceAsync(account, aid, "retry.pdf", new MemoryStream(Pdf())); var failing = new LeasingInvoiceProcessor(factory, new FailingInterpreter(), new Clock()); await failing.ProcessOneAsync();
        var failure = await admin.GetInvoiceAsync(account, broken); Check(failure.Invoice.Processing == LeasingInvoiceProcessing.Failed && failure.Invoice.Status == LeasingInvoiceStatus.Review, "technical failure is separate from business status");
        await admin.RetryInvoiceAsync(account, broken, failure.Invoice.Revision); await processor.ProcessOneAsync(); Check((await admin.GetInvoiceAsync(account, broken)).Invoice.Processing == LeasingInvoiceProcessing.Ready, "technical failure can safely retry");
        try { await admin.UploadInvoiceAsync(account, aid, "disguised.pdf", new MemoryStream("not a PDF"u8.ToArray())); throw new Exception("Invalid signature accepted"); } catch (AgreementFileException) { Check(true, "actual file type is validated"); }
    }
    private sealed class FailingInterpreter : IInvoiceDocumentInterpreter { public Task<InvoiceInterpretationResult> InterpretAsync(LeasingInvoice invoice, CancellationToken ct) => throw new IOException("test failure"); }
    private sealed class FakeHttp : HttpMessageHandler
    {
        public int Calls; public string LastRequest = ""; public InvoiceData Result = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; LastRequest = await request.Content!.ReadAsStringAsync(ct); return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(Result) } } } } })) }; }
    }
    public static string Ubl(string number, decimal quantity, decimal price, bool credit = false)
    {
        var net = InvoiceCalculator.Money(quantity * price); var vat = InvoiceCalculator.Money(net * .25m); var root = credit ? "CreditNote" : "Invoice";
        string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        return $"""
        <{root} xmlns="urn:oasis:names:specification:ubl:schema:xsd:{root}-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
        <cbc:UBLVersionID>2.1</cbc:UBLVersionID><cbc:ID>{number}</cbc:ID><cbc:IssueDate>2027-03-15</cbc:IssueDate><cbc:{(credit ? "CreditNoteTypeCode" : "InvoiceTypeCode")}>{(credit ? "381" : "380")}</cbc:{(credit ? "CreditNoteTypeCode" : "InvoiceTypeCode")}><cbc:DocumentCurrencyCode>NOK</cbc:DocumentCurrencyCode>
        <cac:AccountingSupplierParty><cac:Party><cac:PartyLegalEntity><cbc:RegistrationName>Synthetic supplier</cbc:RegistrationName><cbc:CompanyID>123456789</cbc:CompanyID></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>
        <cac:TaxTotal><cbc:TaxAmount currencyID="NOK">{N(vat)}</cbc:TaxAmount><cac:TaxSubtotal><cbc:TaxableAmount>{N(net)}</cbc:TaxableAmount><cbc:TaxAmount>{N(vat)}</cbc:TaxAmount><cac:TaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>25</cbc:Percent></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
        <cac:LegalMonetaryTotal><cbc:TaxExclusiveAmount>{N(net)}</cbc:TaxExclusiveAmount><cbc:TaxInclusiveAmount>{N(net+vat)}</cbc:TaxInclusiveAmount><cbc:PayableAmount>{N(net+vat)}</cbc:PayableAmount></cac:LegalMonetaryTotal>
        <cac:{(credit ? "CreditNoteLine" : "InvoiceLine")}><cbc:ID>1</cbc:ID><cbc:{(credit ? "CreditedQuantity" : "InvoicedQuantity")} unitCode="EA">{N(quantity)}</cbc:{(credit ? "CreditedQuantity" : "InvoicedQuantity")}><cbc:LineExtensionAmount>{N(net)}</cbc:LineExtensionAmount><cac:Item><cbc:Name>Equipment</cbc:Name><cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>25</cbc:Percent></cac:ClassifiedTaxCategory></cac:Item><cac:Price><cbc:PriceAmount>{N(price)}</cbc:PriceAmount><cbc:BaseQuantity>1</cbc:BaseQuantity></cac:Price></cac:{(credit ? "CreditNoteLine" : "InvoiceLine")}>
        </{root}>
        """;
    }
    private static byte[] Pdf()
    {
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Contents 5 0 R >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Contents 5 0 R >>", "<< /Length 0 >>\nstream\n\nendstream" };
        for (int i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length; text.Append("xref\n0 6\n0000000000 65535 f \n"); foreach (var offset in offsets) text.Append(offset.ToString("D10") + " 00000 n \n");
        text.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return Encoding.ASCII.GetBytes(text.ToString());
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("INVOICE FAIL: " + message); Console.WriteLine("PASS INVOICE: " + message); }
    private static async Task Reject(Func<Task> action, string key) { try { await action(); throw new Exception("Expected " + key); } catch (LeasingValidationException ex) when (ex.Message == key) { Check(true, key); } }
    private static async Task Deny(Func<Task> action) { try { await action(); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { Check(true, "tenant/owner access denied"); } }
}
