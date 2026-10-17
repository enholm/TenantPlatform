using TenantPlatform.Application.Agreements;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Leasing;
using TenantPlatform.Infrastructure.Persistence;

static class LeasingApplicationChecks
{
    public static async Task Run(Factory factory, Guid admin, Guid owner, Guid outsider, Guid foreignAccount)
    {
        var account = Guid.NewGuid(); var framework = Guid.NewGuid(); var task = Guid.NewGuid(); var note = Guid.NewGuid();
        var dimension = Guid.NewGuid(); var foreignDimension = Guid.NewGuid(); var party = Guid.NewGuid();
        var adminMembership = Guid.NewGuid(); var ownerMembership = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id = account, Name = "Direct leasing application" });
            db.Organizations.Add(new() { Id = party, AccountId = account, Name = "Finance" });
            db.UserAccounts.AddRange(new() { Id = adminMembership, AccountId = account, UserId = admin }, new() { Id = ownerMembership, AccountId = account, UserId = owner }, new() { Id = Guid.NewGuid(), AccountId = account, UserId = outsider });
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = adminMembership, Role = UserRole.AccountAdmin });
            db.LeasingFrameworks.Add(new() { Id = framework, AccountId = account, OwnerUserId = owner, FinanceOrganizationId = party, Name = "Owned framework", Number = "DIRECT", AcquisitionFrom = new(2028,1,1), AcquisitionTo = new(2028,12,31), Limit = 100, Revision = Guid.NewGuid() });
            db.Dimensions.AddRange(new() { Id = dimension, AccountId = account, Name = "Direct", Code = "DIRECT" }, new() { Id = foreignDimension, AccountId = foreignAccount, Name = "Foreign direct", Code = "DIRECT" });
            db.LeasingFollowups.Add(new() { Id = task, AccountId = account, FrameworkId = framework, Key = "direct-test", Kind = LeasingFollowupKind.UnknownTerms, Status = LeasingFollowupStatus.Open, Revision = Guid.NewGuid() });
            db.LeasingNotifications.Add(new() { Id = note, AccountId = account, FollowupId = task, RecipientUserId = owner, Status = LeasingNotificationStatus.Delivered, ScheduledUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, owner, account)); var guard = new SaveGuard();
        DbContextOptions<TenantPlatformDbContext> options;
        await using (var db = factory.CreateDbContext()) options = new DbContextOptionsBuilder<TenantPlatformDbContext>((DbContextOptions<TenantPlatformDbContext>)db.GetService<IDbContextOptions>()).AddInterceptors(guard).Options;
        var guarded = new Factory(options); var clock = new Clock();
        var authorization = new AuthorizationService(actor, new AuthorizationData(guarded), clock);
        var reader = new LeasingReader(authorization, new LeasingReadData(guarded), clock);
        var classification = new LeasingClassificationService(authorization, new LeasingClassificationData(guarded), clock);
        var frameworks = new LeasingFrameworkWriter(authorization, new LeasingFrameworkWriteData(guarded), clock);
        var followups = new LeasingFollowupService(authorization, new LeasingFollowupData(guarded), new LeasingLifecycleJob(new LeasingLifecycleJobData(guarded), clock, new HostSystemWorkContext(SystemWorkKind.LeasingLifecycle)), clock);
        guard.Enabled = true;
        Check((await reader.OptionsAsync(account)).Frameworks.Single().Id == framework, "direct options use concrete framework ownership");
        Check((await reader.ListAsync(account, null, "frameworks", null, null, 1)).Items.Single().Id == framework, "direct list applies owned SQL scope before paging");
        Check((await reader.GetAsync(account, framework, true)).CanEdit, "direct detail recognizes framework owner");
        Check((await reader.NewAcquisitionAsync(account, framework)).OwnerUserId == owner, "new acquisition uses trusted actor without writes");
        await Denied(() => reader.NewAcquisitionAsync(account, null));
        await Denied(() => reader.GetAsync(account, Guid.NewGuid(), true));
        actor.Current = new(true, outsider, account);
        await Denied(() => reader.OptionsAsync(account));
        await Denied(() => reader.GetAsync(account, framework, true));
        await Denied(() => reader.NewAcquisitionAsync(account, framework));
        Check((await reader.ListAsync(account, null, "frameworks", null, null, 1)).Count == 0, "ordinary member has no inaccessible list amounts");
        actor.Current = new(true, owner, account);

        var catalog = await classification.ClassificationCatalogAsync(account);
        Check(!catalog.CanManage && catalog.Dimensions.All(x => x.Dimension.AccountId == account), "owned-root catalog has account-only metadata without writes");
        await Denied(() => followups.GetLeasingNotificationSettingsAsync(account));
        await Denied(() => followups.LeasingNotificationHistoryAsync(account));
        actor.Current = new(true, admin, account);
        Check((await followups.GetLeasingNotificationSettingsAsync(account)).Revision == Guid.Empty, "missing notification settings return an in-memory default");
        Check((await followups.LeasingNotificationHistoryAsync(account)).Count == 1, "administrator history is a pure read");
        guard.Enabled = false;
        LeasingFramework frame;
        await using (var db = factory.CreateDbContext()) frame = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.AccountId == account && x.Id == framework);
        actor.Current = new(true, owner, account);
        frame.Name = "Owned direct update";
        await frameworks.SaveFrameworkAsync(account, framework, frame, "Trusted owner update");
        await Invalid(() => frameworks.SaveFrameworkAsync(account, framework, frame, "Stale owner update"), "LeasingConcurrency");
        await Denied(() => frameworks.SaveFrameworkAsync(account, null, frame, "Owner cannot create"));
        await using (var db = factory.CreateDbContext()) frame = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.Id == framework);
        frame.OwnerUserId = outsider;
        await Denied(() => frameworks.SaveFrameworkAsync(account, framework, frame, "Unauthorized transfer"));
        actor.Current = new(true, admin, account);
        frame.OwnerUserId = owner; frame.FinanceOrganizationId = Guid.NewGuid();
        await Invalid(() => frameworks.SaveFrameworkAsync(account, framework, frame, "Foreign finance"), "LeasingInvalidParty");
        frame.FinanceOrganizationId = party; frame.Number = "DIRECT-CREATED"; frame.AccountId = foreignAccount; frame.Id = Guid.NewGuid();
        var created = await frameworks.SaveFrameworkAsync(account, null, frame, "Direct administrator create");
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingFrameworks.AnyAsync(x => x.Id == created && x.AccountId == account) && await db.LeasingHistory.AnyAsync(x => x.FrameworkId == created && x.ActorUserId == admin), "framework creation ignores client account/ID and records trusted actor");
        var foreignPerson = Guid.NewGuid(); var privateDocument = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new() { Id = foreignPerson, Email = foreignPerson + "@example.test", FirstName = "Foreign private name", IsActive = true });
            db.UserAccounts.Add(new() { Id = Guid.NewGuid(), AccountId = foreignAccount, UserId = foreignPerson });
            (await db.LeasingFrameworks.SingleAsync(x => x.Id == created)).OwnerUserId = foreignPerson;
            db.LeasingDocuments.Add(new() { Id = privateDocument, AccountId = account, FrameworkId = created, FileName = "private.pdf", MediaType = "application/pdf", StorageKey = "INTERNAL-STORAGE-KEY", Size = 10, UploadedByUserId = admin, UploadedUtc = clock.GetUtcNow() });
            db.LeasingHistory.Add(new() { Id = Guid.NewGuid(), AccountId = account, FrameworkId = created, ActorUserId = foreignPerson, RecordedUtc = clock.GetUtcNow(), Action = "LegacyForeignActor" });
            await db.SaveChangesAsync();
        }
        guard.Enabled = true;
        var sanitized = await reader.GetAsync(account, created, true);
        Check(sanitized.OwnerName == "" && sanitized.History.Single(x => x.Action == "LegacyForeignActor").Actor == "" && sanitized.Documents.Single(x => x.Id == privateDocument).StorageKey == "", "details retain foreign/missing-person history while hiding names and internal document keys");
        guard.Enabled = false;
        actor.Current = new(true, outsider, account);
        await Denied(() => frameworks.SaveFrameworkAsync(account, framework, frame, "Unowned framework"));
        actor.Current = new(true, admin, account);
        var rule = new LeasingDimensionRule { DimensionId = dimension, IsEnabled = true, IsRequired = true };
        await classification.SaveDimensionRuleAsync(account, rule);
        await Invalid(() => classification.SaveDimensionRuleAsync(account, rule), "LeasingConcurrency");
        await Denied(() => classification.SaveDimensionRuleAsync(account, new() { DimensionId = foreignDimension }));
        var settings = await followups.GetLeasingNotificationSettingsAsync(account);
        settings.ExtraRecipientIds = [Guid.NewGuid()];
        await Invalid(() => followups.SaveLeasingNotificationSettingsAsync(account, settings, "Invalid recipient"), "LifeInvalidOwner");
        settings.ExtraRecipientIds = [owner]; settings.Enabled = true;
        await followups.SaveLeasingNotificationSettingsAsync(account, settings, "Direct activation");
        await Invalid(() => followups.SaveLeasingNotificationSettingsAsync(account, new(), "Stale settings"), "LeasingConcurrency");
        await using (var db = factory.CreateDbContext())
        {
            var membership = await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == outsider);
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin, OrganizationId = party });
            db.Users.Single(x => x.Id == outsider).IsPlatformAdmin = true;
            await db.SaveChangesAsync();
        }
        actor.Current = new(true, outsider, account);
        await Denied(() => reader.GetAsync(account, framework, true));
        await Denied(() => reader.OptionsAsync(account));
        await Denied(() => frameworks.SaveFrameworkAsync(account, null, frame, "Scoped/global admin cannot create"));
        Check(true, "global platform flag and scoped AccountAdmin do not broaden leasing actions or reader scope");
        actor.Current = new(true, outsider, account);
        await Denied(() => classification.ClassificationCatalogAsync(account));
        await Denied(() => classification.SaveDimensionRuleAsync(account, rule));
        await Denied(() => followups.SaveLeasingNotificationSettingsAsync(account, settings, "Not administrator"));
        Guid revision;
        await using (var db = factory.CreateDbContext()) revision = await db.LeasingFollowups.Where(x => x.Id == task).Select(x => x.Revision).SingleAsync();
        await Denied(() => followups.UpdateLeasingFollowupAsync(account, task, revision, LeasingFollowupStatus.Completed, null, "Not owner", Guid.NewGuid()));
        await Denied(() => followups.ReadLeasingNotificationAsync(account, note));
        actor.Current = new(true, owner, account);
        await Invalid(() => followups.UpdateLeasingFollowupAsync(account, task, revision, LeasingFollowupStatus.Completed, foreignPerson, "Bad assignee", Guid.NewGuid()), "LifeInvalidOwner");
        var request = Guid.NewGuid();
        await followups.UpdateLeasingFollowupAsync(account, task, revision, LeasingFollowupStatus.Completed, owner, "Direct completion", request);
        await followups.UpdateLeasingFollowupAsync(account, task, revision, LeasingFollowupStatus.Completed, owner, "Retry", request);
        await followups.ReadLeasingNotificationAsync(account, note);
        await followups.ReadLeasingNotificationAsync(account, note);
        await using (var db = factory.CreateDbContext())
        {
            Check(await db.LeasingPaymentEvents.CountAsync(x => x.AccountId == account && x.Id == request && x.ActorUserId == owner) == 1, "direct completion uses trusted actor and idempotency under account lock");
            Check(await db.LeasingNotifications.AnyAsync(x => x.Id == note && x.ReadUtc != null), "authorized notification read persists once");
            Check(await db.DimensionHistory.AnyAsync(x => x.AccountId == account && x.DimensionId == dimension && x.ActorUserId == admin), "dimension rule keeps trusted history actor");
        }
        var page = await followups.ListLeasingFollowupsAsync(account);
        Check(page.Rows.All(x => x.AccountId == account) && page.Names.Keys.All(x => x == framework || x == created), "direct list preserves backfill and authorized parent scope");
        var documentStorage = new Documents();
        var documentService = new LeasingDocumentService(authorization, new LeasingDocumentData(guarded, documentStorage, NullLogger<LeasingDocumentData>.Instance), documentStorage, new PaymentPlanSourceReader(), clock);
        var acquisition = Guid.NewGuid(); var order = Guid.NewGuid(); var acquisitionRevision = Guid.NewGuid(); var orderRevision = Guid.NewGuid(); Guid frameRevision;
        await using (var db = factory.CreateDbContext())
        {
            frameRevision = await db.LeasingFrameworks.Where(x => x.Id == framework).Select(x => x.Revision).SingleAsync();
            db.LeasingAcquisitions.Add(new() { Id = acquisition, AccountId = account, FrameworkId = framework, OwnerUserId = owner, FinanceOrganizationId = party, SupplierOrganizationId = party, Name = "Document target", Reference = "DOCUMENT", PurchaseDate = new(2028,1,1), EndDate = new(2030,1,1), Revision = acquisitionRevision, Status = LeasingAcquisitionStatus.Draft });
            db.LeasingOrders.Add(new() { Id = order, AccountId = account, FrameworkId = framework, OwnerUserId = owner, SupplierOrganizationId = party, Number = "DOCUMENT", OrderDate = new(2028,1,1), Status = LeasingOrderStatus.Draft, Revision = orderRevision });
            await db.SaveChangesAsync();
        }
        await Denied(() => documentService.DownloadAsync(account, privateDocument));
        await Denied(() => documentService.ReadPaymentPlanSourceAsync(account, acquisition, privateDocument));
        await documentService.UploadAsync(account, framework, true, frameRevision, "frame.pdf", new MemoryStream([1,2,3]));
        await documentService.UploadAsync(account, acquisition, false, acquisitionRevision, "acquisition.pdf", new MemoryStream([1,2,3]));
        await documentService.UploadOrderAsync(account, order, orderRevision, "order.pdf", new MemoryStream([1,2,3]));
        const string csv = "Ref;From;To;Due;Net;Vat;Gross;Type\nONE;01.01.2028;31.01.2028;01.02.2028;100;25;125;Rent\n";
        var imported = await documentService.UploadPaymentPlanSourceAsync(account, acquisition, "plan.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        guard.Enabled = true;
        var grid = await documentService.ReadPaymentPlanSourceAsync(account, acquisition, imported.Document);
        Check(PaymentPlanImportRules.Preview(grid, new(), "NOK").Terms.Single().Gross == 125, "direct payment source upload/read uses technical parser and Application mapping rules");
        Guid frameDocument;
        await using (var db = factory.CreateDbContext()) frameDocument = await db.LeasingDocuments.Where(x => x.AccountId == account && x.FrameworkId == framework).Select(x => x.Id).SingleAsync();
        await using (var download = (await documentService.DownloadAsync(account, frameDocument)).Content) Check(download.Length == 3, "direct original download is authorized and write-free");
        actor.Current = new(true, outsider, account);
        await Denied(() => documentService.DownloadAsync(account, frameDocument));
        await Denied(() => documentService.ReadPaymentPlanSourceAsync(account, acquisition, imported.Document));
        await Denied(() => documentService.UploadOrderAsync(account, order, orderRevision, "denied.pdf", new MemoryStream([1])));
        actor.Current = new(true, owner, account); guard.Enabled = false;
        Guid latestRevision;
        await using (var db = factory.CreateDbContext()) latestRevision = await db.LeasingFrameworks.Where(x => x.Id == framework).Select(x => x.Revision).SingleAsync();
        var storedBefore = documentStorage.Count;
        documentStorage.AfterStore = () => actor.Current = new(false, owner, account);
        await Denied(() => documentService.UploadAsync(account, framework, true, latestRevision, "revoked.pdf", new MemoryStream([4])));
        Check(documentStorage.Count == storedBefore, "revocation during upload leaves no uncommitted file");
        actor.Current = new(true, owner, account);
        await using (var db = factory.CreateDbContext()) Check(await db.LeasingFrameworks.Where(x => x.Id == framework).Select(x => x.Revision).SingleAsync() == latestRevision && !await db.LeasingDocuments.AnyAsync(x => x.AccountId == account && x.FileName == "revoked.pdf"), "revoked upload changes neither revision nor document metadata");
        documentStorage.AfterStore = null;
        actor.Current = new(true, owner, foreignAccount);
        await Denied(() => followups.ListLeasingFollowupsAsync(account));
        await Denied(() => classification.ClassificationCatalogAsync(account));
        actor.Current = new(false, owner, account);
        await Denied(() => followups.ListLeasingFollowupsAsync(account));
        actor.Current = new(true, owner, account);
        await using (var db = factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == ownerMembership)); await db.SaveChangesAsync(); }
        await Denied(() => followups.ListLeasingFollowupsAsync(account));
        await Denied(() => followups.ReadLeasingNotificationAsync(account, note));
        await Denied(() => classification.ClassificationCatalogAsync(account));
        await Denied(() => frameworks.SaveFrameworkAsync(account, framework, frame, "Revoked membership"));
        Check(true, "membership revocation is revalidated by every new application call");
        await using (var db = factory.CreateDbContext()) { db.Users.Single(x => x.Id == outsider).IsPlatformAdmin = false; await db.SaveChangesAsync(); }

    }
    private sealed class Documents : IAgreementDocumentStorage
    {
        private readonly Dictionary<string, byte[]> files = [];
        public Action? AfterStore; public int Count => files.Count; public long MaxFileSizeBytes => 1_000_000;
        public async Task<StoredAgreementFile> StoreAsync(Stream source, string name, CancellationToken ct = default) { using var copy = new MemoryStream(); await source.CopyToAsync(copy, ct); var key = Guid.NewGuid().ToString("N"); files[key] = copy.ToArray(); AfterStore?.Invoke(); return new(key, name, "application/octet-stream", copy.Length); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(files[key]));
        public Task DiscardUncommittedAsync(string key) { files.Remove(key); return Task.CompletedTask; }
    }
    private sealed class Actor(ActorSnapshot current) : ICurrentActor { public ActorSnapshot Current = current; public ActorSnapshot GetSnapshot() => Current; }
    private sealed class SaveGuard : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) => Enabled ? throw new Exception("Direct leasing reader attempted SaveChanges") : base.SavingChangesAsync(data, result, ct);
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL direct leasing: " + name); Console.WriteLine("PASS direct leasing: " + name); }
    private static async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected direct access denial"); } catch (UnauthorizedAccessException) { } }
    private static async Task Invalid(Func<Task> action, string key) { try { await action(); throw new Exception("Expected " + key); } catch (LeasingValidationException ex) when (ex.Message == key) { } }
}
