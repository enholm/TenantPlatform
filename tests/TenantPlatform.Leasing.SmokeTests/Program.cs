using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
Check(LeasingCalculator.EndDate(new(2027, 1, 15), 60) == new DateOnly(2032, 1, 15), "January acquisition has individual five-year end date");
Check(LeasingCalculator.EndDate(new(2027, 12, 20), 60) == new DateOnly(2032, 12, 20), "December acquisition has different five-year end date");
Check(LeasingCalculator.EndDate(new(2028, 2, 29), 12) == new DateOnly(2029, 2, 28), "leap day clamps to February month end");
Check(LeasingCalculator.EndDate(new(2027, 1, 31), 1) == new DateOnly(2027, 2, 28), "month end clamps without subtracting a day");
Check(LeasingCalculator.Line(new() { Quantity = 1, UnitPrice = 0.025m, VatPercent = 25 }) == new LeasingTotals(.03m, .01m), "decimal line net/VAT rounding uses midpoint away from zero");
var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LEASING_TEST_CONNECTION") ?? throw new Exception("Set LEASING_TEST_CONNECTION to the disposable tenant_leasing_tests database."));
if (cs.Database != "tenant_leasing_tests") throw new Exception("Only tenant_leasing_tests is allowed.");
var schema = "leasing_" + Guid.NewGuid().ToString("N");
await using var setup = new NpgsqlConnection(cs.ConnectionString); await setup.OpenAsync();
await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
try
{
    cs.SearchPath = schema;
    var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString).Options);
    await using (var db = factory.CreateDbContext())
    {
        Check(!db.Database.HasPendingModelChanges(), "EF model matches migration snapshot");
        await db.Database.MigrateAsync();
    }
    var account = Guid.NewGuid(); var foreignAccount = Guid.NewGuid(); var adminId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var outsiderId = Guid.NewGuid();
    var party = Guid.NewGuid(); var foreignParty = Guid.NewGuid();
    await using (var db = factory.CreateDbContext())
    {
        db.Accounts.AddRange(new Account { Id = account, Name = "Leasing test" }, new Account { Id = foreignAccount, Name = "Other account" });
        foreach (var user in new[] { adminId, ownerId, outsiderId })
        {
            db.Users.Add(new User { Id = user, Email = user + "@example.test", FirstName = "Test", IsActive = true });
            var member = new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = user }; db.UserAccounts.Add(member);
            if (user == adminId) db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = member.Id, Role = UserRole.AccountAdmin });
        }
        db.Organizations.AddRange(new Organization { Id = party, AccountId = account, Name = "Finance / Supplier" }, new Organization { Id = foreignParty, AccountId = foreignAccount, Name = "Foreign supplier" });
        await db.SaveChangesAsync();
    }
    var storage = new MemoryStorage();
    LeasingService Service(Guid user) { var context = new Context(user, account); return new(factory, context, new TenantAuthorizationService(factory, context), storage, new Clock(), NullLogger<LeasingService>.Instance); }
    var admin = Service(adminId); var owner = Service(ownerId); var outsider = Service(outsiderId);
    LeasingFramework Frame(decimal limit = 100, bool vat = false) => new()
    {
        Name = "Test framework", Number = Guid.NewGuid().ToString("N"), OwnerUserId = adminId, FinanceOrganizationId = party,
        AcquisitionFrom = new(2027, 1, 1), AcquisitionTo = new(2027, 12, 31), Limit = limit, IncludesVat = vat,
        Terms = new() { Months = 60, InterestKind = LeasingInterestKind.Reference, ReferenceRateName = "NIBOR 3 måneder", MarginPercentagePoints = 2, PaymentFrequency = LeasingPaymentFrequency.Yearly }
    };
    async Task<LeasingAcquisition> Purchase(Guid? f, decimal amount, DateOnly? date = null, LeasingAcquisitionStatus status = LeasingAcquisitionStatus.Registered)
    {
        var a = await admin.NewAcquisitionAsync(account, f); a.Name = "Laptops"; a.Reference = Guid.NewGuid().ToString("N");
        a.SupplierOrganizationId = a.FinanceOrganizationId = party; a.OwnerUserId = ownerId; a.PurchaseDate = date ?? new(2027, 6, 1);
        a.InvoiceNumber = "INV-001"; a.InvoiceDate = a.PurchaseDate; a.FinancedAmount = Math.Max(.01m, decimal.Round(amount / 2, 2));
        a.Items = [new() { Description = "Laptop", Quantity = 1, UnitPrice = amount, VatPercent = 25 }]; a.Status = status; return a;
    }
    async Task<LeasingAcquisition> Read(Guid id) => (await admin.GetAsync(account, id, false)).Acquisition!;
    async Task<LeasingFramework> ReadF(Guid id) => (await admin.GetAsync(account, id, true)).Framework!;
    async Task<decimal> Used(Guid id) => (await admin.GetAsync(account, id, true)).Used;
    var framework = await admin.SaveFrameworkAsync(account, null, Frame(), "");
    var first = await admin.SaveAcquisitionAsync(account, null, await Purchase(framework, 40, new(2027, 1, 1)), "");
    var last = await admin.SaveAcquisitionAsync(account, null, await Purchase(framework, 60, new(2027, 12, 31)), "");
    Check(await Used(framework) == 100, "both inclusive purchase boundaries, exact capacity allowed; financed amount is separate");
    var excess = await Purchase(framework, 1);
    await Reject(() => admin.SaveAcquisitionAsync(account, null, excess, ""), "LeasingLimitExceeded");
    foreach (var date in new[] { new DateOnly(2026, 12, 31), new DateOnly(2028, 1, 1) })
    { var input = await Purchase(framework, 1, date); await Reject(() => admin.SaveAcquisitionAsync(account, null, input, ""), "LeasingOutsidePeriod"); }
    var draft = await Purchase(framework, 500, status: LeasingAcquisitionStatus.Draft); draft.InvoiceDate = null; draft.InvoiceNumber = null;
    var draftId = await admin.SaveAcquisitionAsync(account, null, draft, ""); Check(await Used(framework) == 100, "draft with no invoice does not consume capacity");
    draft = await Read(draftId); draft.Status = LeasingAcquisitionStatus.Registered;
    await Reject(() => admin.SaveAcquisitionAsync(account, draftId, draft, "Register"), "LeasingRegistrationRequired");
    var edited = await Read(first); var lineId = edited.Items[0].Id; edited.Items[0].UnitPrice = 41;
    await Reject(() => admin.SaveAcquisitionAsync(account, first, edited, "Increase"), "LeasingLimitExceeded");
    edited.Items[0].UnitPrice = 30; await owner.SaveAcquisitionAsync(account, first, edited, "Correct purchase value");
    Check(await Used(framework) == 90 && (await Read(first)).Items[0].Id == lineId, "registered edit recalculates capacity and preserves item ID");
    await Reject(() => admin.SaveAcquisitionAsync(account, first, edited, "Stale save"), "LeasingConcurrency");
    var fedit = await ReadF(framework); fedit.Limit = 89;
    await Reject(() => admin.SaveFrameworkAsync(account, framework, fedit, "Reduce"), "LeasingLimitExceeded");
    fedit = await ReadF(framework); fedit.IncludesVat = true;
    await Reject(() => admin.SaveFrameworkAsync(account, framework, fedit, "VAT basis"), "LeasingFrameworkConflict");
    fedit = await ReadF(framework); fedit.Currency = "EUR";
    await Reject(() => admin.SaveFrameworkAsync(account, framework, fedit, "Currency"), "LeasingFrameworkConflict");
    fedit = await ReadF(framework); fedit.AcquisitionFrom = new(2027, 1, 2);
    await Reject(() => admin.SaveFrameworkAsync(account, framework, fedit, "Period"), "LeasingFrameworkConflict");
    fedit = await ReadF(framework); fedit.Terms.Months = 12; fedit.Terms.MarginPercentagePoints = 3;
    await admin.SaveFrameworkAsync(account, framework, fedit, "New standard terms");
    Check((await Read(first)).Terms.Months == 60 && (await Read(first)).Terms.MarginPercentagePoints == 2 &&
        (await Read(first)).Terms.PaymentFrequency == LeasingPaymentFrequency.Yearly, "registered terms remain snapshots; reference tenor does not set frequency");
    Check((await admin.NewAcquisitionAsync(account, framework)).Terms.Months == 12, "new acquisitions copy updated defaults");
    await admin.CancelAsync(account, last, (await Read(last)).Revision, "Cancelled purchase");
    Check(await Used(framework) == 30 && (await Read(last)).Status == LeasingAcquisitionStatus.Cancelled, "cancellation releases capacity and retains acquisition");
    var details = await admin.GetAsync(account, last, false);
    Check(details.History.Any(x => x.Action == "Cancelled" && x.BeforeJson.Contains("60") && x.Reason == "Cancelled purchase"), "cancellation has before/after audit history");
    var cancelled = await Read(last); cancelled.Status = LeasingAcquisitionStatus.Draft;
    await Reject(() => admin.SaveAcquisitionAsync(account, last, cancelled, "Restore"), "LeasingInvalidTransition");
    edited = await Read(first); edited.Status = LeasingAcquisitionStatus.Draft;
    await Reject(() => admin.SaveAcquisitionAsync(account, first, edited, "Back to draft"), "LeasingInvalidTransition");
    fedit = await ReadF(framework); fedit.Status = LeasingFrameworkStatus.Closed; await admin.SaveFrameworkAsync(account, framework, fedit, "Close window");
    var manual = await Purchase(null, 1); manual.FrameworkId = framework;
    await Reject(() => admin.SaveAcquisitionAsync(account, null, manual, ""), "LeasingClosed");
    edited = await Read(first); edited.Notes = "Still accessible after closure"; await owner.SaveAcquisitionAsync(account, first, edited, "Note correction");
    Check((await Read(first)).EndDate == new DateOnly(2032, 1, 1), "closed frameworks do not end existing leases");
    var standalone = await admin.SaveAcquisitionAsync(account, null, await Purchase(null, 25), "");
    Check((await Read(standalone)).FrameworkId == null, "standalone acquisition has no framework");
    var grossFrame = await admin.SaveFrameworkAsync(account, null, Frame(125, true), "");
    var grossLease = await admin.SaveAcquisitionAsync(account, null, await Purchase(grossFrame, 100), "");
    Check(await Used(grossFrame) == 125, "VAT-inclusive frame consumes gross purchase value");
    var tooMuch = await Purchase(grossFrame, .01m); await Reject(() => admin.SaveAcquisitionAsync(account, null, tooMuch, ""), "LeasingLimitExceeded");
    var target = await admin.SaveFrameworkAsync(account, null, Frame(20), "");
    var move = await Read(grossLease); move.FrameworkId = target;
    await Reject(() => admin.SaveAcquisitionAsync(account, grossLease, move, "Move"), "LeasingLimitExceeded");
    Check(await Used(grossFrame) == 125 && await Used(target) == 0, "failed move leaves original and target capacities unchanged");
    var targetEdit = await ReadF(target); targetEdit.Limit = 100; await admin.SaveFrameworkAsync(account, target, targetEdit, "Increase");
    await admin.SaveAcquisitionAsync(account, grossLease, move, "Move between frameworks");
    Check(await Used(grossFrame) == 0 && await Used(target) == 100, "successful move updates both frames atomically");
    var badCurrency = await Purchase(target, 1); badCurrency.Currency = "EUR";
    await Reject(() => admin.SaveAcquisitionAsync(account, null, badCurrency, ""), "LeasingFrameworkCurrency");
    var datedEdit = await Read(first); datedEdit.PurchaseDate = new(2028, 1, 1);
    await Reject(() => owner.SaveAcquisitionAsync(account, first, datedEdit, "Purchase date correction"), "LeasingOutsidePeriod");
    datedEdit.PurchaseDate = new(2027, 1, 31); await owner.SaveAcquisitionAsync(account, first, datedEdit, "Purchase date correction");
    Check((await Read(first)).EndDate == new DateOnly(2032, 1, 31), "registered purchase-date edits recompute individual lease end");
    var extraLine = await Read(standalone); extraLine.Items.Add(new() { Description = "Dock", Quantity = 2, UnitPrice = 10, VatPercent = 0 });
    await owner.SaveAcquisitionAsync(account, standalone, extraLine, "Add dock");
    var reloaded = await Read(standalone);
    Check(reloaded.Items.Count == 2 && reloaded.Items.All(x => x.Id != Guid.Empty) && reloaded.NetTotal == 45, "new item on an existing lease is persisted and totalled");
    reloaded.Items.RemoveAll(x => x.Description == "Dock"); await owner.SaveAcquisitionAsync(account, standalone, reloaded, "Remove dock");
    Check((await Read(standalone)).Items.Count == 1, "line removal is audited and recalculates totals");
    var negative = await Purchase(null, 1); negative.Items[0].UnitPrice = -1;
    await Reject(() => admin.SaveAcquisitionAsync(account, null, negative, ""), "LeasingInvalidLines");
    var injected = await Purchase(null, 1); injected.Items[0].Id = lineId;
    await Reject(() => admin.SaveAcquisitionAsync(account, null, injected, ""), "LeasingInvalidLines");
    var foreign = await Purchase(null, 1); foreign.SupplierOrganizationId = foreignParty;
    await Reject(() => admin.SaveAcquisitionAsync(account, null, foreign, ""), "LeasingInvalidParty");
    await Deny(() => outsider.OptionsAsync(account));
    await Deny(() => outsider.GetAsync(account, first, false)); await Deny(() => admin.GetAsync(foreignAccount, first, false));
    await Deny(() => outsider.SaveAcquisitionAsync(account, null, manual, ""));
    Check((await outsider.ListAsync(account, null, "all", null, null, 1)).Count == 0, "unrelated member sees no leases");
    Check((await owner.ListAsync(account, null, "orders", null, null, 1)).Items.All(x => !x.Framework), "standalone filter and owner access");
    var concurrent = await admin.SaveFrameworkAsync(account, null, Frame(100), "");
    var purchases = new[] { await Purchase(concurrent, 60), await Purchase(concurrent, 60) };
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var tasks = purchases.Select(async p => { await gate.Task; try { await admin.SaveAcquisitionAsync(account, null, p, ""); return true; } catch (LeasingValidationException ex) when (ex.Message == "LeasingLimitExceeded") { return false; } }).ToArray();
    gate.SetResult(); var results = await Task.WhenAll(tasks);
    Check(results.Count(x => x) == 1 && await Used(concurrent) == 60, "simultaneous registrations serialize: exactly one fits and commits");
    var documentParent = await Read(standalone);
    await owner.UploadAsync(account, standalone, false, documentParent.Revision, "invoice.pdf", new MemoryStream([1, 2, 3]));
    var document = (await owner.GetAsync(account, standalone, false)).Documents.Single();
    var file = await owner.DownloadAsync(account, document.Id); await file.Content.DisposeAsync();
    await Deny(() => outsider.DownloadAsync(account, document.Id)); await Deny(() => admin.DownloadAsync(foreignAccount, document.Id));
    Check(storage.Stored == 1, "documents use shared storage with parent/tenant authorization");
    await LeasingComponentChecks.Run(admin, account, adminId, framework, standalone);
    Console.WriteLine("All leasing PostgreSQL smoke checks passed.");
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup); await drop.ExecuteNonQueryAsync();
}
static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
static async Task Reject(Func<Task> action, string key) { try { await action(); throw new Exception("Expected " + key); } catch (LeasingValidationException ex) when (ex.Message == key) { Console.WriteLine("PASS rejection: " + key); } }
static async Task Deny(Func<Task> action) { try { await action(); throw new Exception("Expected access denial"); } catch (UnauthorizedAccessException) { Console.WriteLine("PASS: access denied"); } }
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext> { public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class Context(Guid user, Guid account) : ICurrentUserContextService { public CurrentUserContext Current { get; } = new() { UserId = user, CurrentAccountId = account, IsAuthenticated = true }; }
sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2028, 1, 15, 12, 0, 0, TimeSpan.Zero); }
sealed class MemoryStorage : IAgreementDocumentStorage
{
    public int Stored { get; private set; } public long MaxFileSizeBytes => 1024 * 1024;
    public Task<StoredAgreementFile> StoreAsync(Stream source, string fileName, CancellationToken ct = default) { Stored++; return Task.FromResult(new StoredAgreementFile(Guid.NewGuid().ToString("N"), fileName, "application/pdf", 3)); }
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    public Task DiscardUncommittedAsync(string key) { Stored--; return Task.CompletedTask; }
}
