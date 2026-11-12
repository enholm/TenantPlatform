using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Web.Services.Dimensions;

var connection = Environment.GetEnvironmentVariable("AGREEMENT_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set AGREEMENT_TEST_CONNECTION to a disposable PostgreSQL database.");
var builder = new NpgsqlConnectionStringBuilder(connection);
if (builder.Database != "tenant_agreement_tests")
    throw new InvalidOperationException("The database must be named tenant_agreement_tests.");
var schema = "agreement_test_" + Guid.NewGuid().ToString("N");
Console.WriteLine($"Test database: {builder.Database}");
Console.WriteLine($"Test schema: {schema}");
await using (var setup = new NpgsqlConnection(connection))
{
    await setup.OpenAsync();
    await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup);
    await command.ExecuteNonQueryAsync();
}
builder.SearchPath = schema;
var factory = new TestFactory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(builder.ConnectionString).Options);
await using (var db = factory.CreateDbContext())
{
    Assert(!db.Database.HasPendingModelChanges(), "migration snapshot matches model");
    await db.Database.MigrateAsync();
    Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies to isolated database");
}
var root = Path.Combine(Path.GetTempPath(), "agreement-files-" + Guid.NewGuid().ToString("N"));
Console.WriteLine($"File fixtures: {root}");
var storage = new LocalAgreementDocumentStorage(Options.Create(new AgreementDocumentStorageOptions { RootPath = root, MaxFileSizeBytes = 1024 }));
var a = Guid.NewGuid(); var b = Guid.NewGuid();
var adminId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var readerId = Guid.NewGuid(); var editorId = Guid.NewGuid();
var strangerId = Guid.NewGuid(); var foreignId = Guid.NewGuid(); var propertyAdminId = Guid.NewGuid();
var orgA = Guid.NewGuid(); var orgB = Guid.NewGuid(); var buildingA = Guid.NewGuid(); var buildingB = Guid.NewGuid();
var unitA = Guid.NewGuid(); var unitB = Guid.NewGuid(); var otherBuilding = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Accounts.AddRange(new Account { Id = a, Name = "A" }, new Account { Id = b, Name = "B" });
    foreach (var id in new[] { adminId, ownerId, readerId, editorId, strangerId, foreignId, propertyAdminId })
    {
        db.Users.Add(new User { Id = id, FirstName = id == ownerId ? "Owner" : "Member", Email = $"{id}@example.test" });
        var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = id == foreignId ? b : a, UserId = id };
        db.UserAccounts.Add(membership);
        if (id == adminId || id == foreignId || id == propertyAdminId)
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id,
                Role = id == propertyAdminId ? UserRole.PropertyAdmin : UserRole.AccountAdmin,
                BuildingId = id == propertyAdminId ? buildingA : null });
    }
    db.Organizations.AddRange(new Organization { Id = orgA, AccountId = a, Name = "Counterparty A" },
        new Organization { Id = orgB, AccountId = b, Name = "Counterparty B" });
    db.Buildings.AddRange(new Building { Id = buildingA, AccountId = a, Name = "A" },
        new Building { Id = otherBuilding, AccountId = a, Name = "Other A" }, new Building { Id = buildingB, AccountId = b, Name = "B" });
    db.Units.AddRange(new Unit { Id = unitA, AccountId = a, BuildingId = buildingA, Name = "A" },
        new Unit { Id = unitB, AccountId = b, BuildingId = buildingB, Name = "B" });
    await db.SaveChangesAsync();
}
var admin = Service(adminId, a); var owner = Service(ownerId, a); var reader = Service(readerId, a);
var editor = Service(editorId, a); var stranger = Service(strangerId, a); var foreign = Service(foreignId, b);
var idA = await admin.CreateAsync(a, Request());
var foreignRequest = Request(); foreignRequest.OwnerUserId = foreignId; foreignRequest.CounterpartyOrganizationId = orgB;
var idB = await foreign.CreateAsync(b, foreignRequest);
Assert((await owner.GetAsync(a, idA)).CanManageAccess, "owner can manage without account role");
Assert((await admin.ListAsync(a, new())).Items.All(x => x.Id != idB), "account list isolated");
await Expect<UnauthorizedAccessException>(() => admin.GetAsync(a, idB), "foreign agreement read denied");
await Expect<UnauthorizedAccessException>(() => admin.UpdateAsync(a, idB, Request()), "foreign agreement edit denied");
await Expect<UnauthorizedAccessException>(() => admin.ListAsync(b, new()), "forged account denied");
await Expect<UnauthorizedAccessException>(() => stranger.GetAsync(a, idA), "ungranted member denied");
Assert((await stranger.ListAsync(a, new())).TotalCount == 0, "ungranted list empty");
await Expect<UnauthorizedAccessException>(() => Service(propertyAdminId, a).GetAsync(a, idA), "building administrator gains no agreement access");
await Expect<UnauthorizedAccessException>(() => owner.CreateAsync(a, Request()), "only account admin creates");
var platformContext = new TestUserContext(strangerId, a, true);
var platformAuth = new TenantAuthorizationService(factory, platformContext);
Assert(!await platformAuth.CanCreateAgreementAsync(), "platform role alone does not create agreements");
Assert(!await platformAuth.CanUseAgreementsAsync(), "ungranted member has no menu access");
var ownerAuth = new TenantAuthorizationService(factory, new TestUserContext(ownerId, a));
Assert(await ownerAuth.CanUseAgreementsAsync(), "owner has menu access");

await Invalid(r => r.CounterpartyOrganizationId = orgB, "foreign counterparty");
await Invalid(r => r.OwnerUserId = foreignId, "foreign owner");
await Invalid(r => r.BuildingId = buildingB, "foreign building");
await Invalid(r => { r.BuildingId = buildingA; r.UnitId = unitB; }, "foreign unit");
await Invalid(r => { r.BuildingId = otherBuilding; r.UnitId = unitA; }, "unit in different building");
await Invalid(r => r.UnitId = unitA, "unit without building");
await Invalid(r => r.EndDate = r.StartDate.AddDays(-1), "end before start");
await Invalid(r => { r.AutoRenew = true; r.RenewalMonths = 0; }, "zero renewal");
await Invalid(r => { r.AutoRenew = true; r.RenewalMonths = -1; }, "negative renewal");
await Invalid(r => r.RenewalMonths = 12, "renewal without auto renewal");
var earlyNotice = Request(); earlyNotice.NoticeMode = AgreementNoticeMode.Manual; earlyNotice.NoticeDeadline = earlyNotice.StartDate.AddDays(-30);
var earlyId = await admin.CreateAsync(a, earlyNotice);
Assert((await admin.GetAsync(a, earlyId)).NoticeDeadline == earlyNotice.NoticeDeadline, "notice before start and open-ended agreement supported");
var linked = Request(); linked.BuildingId = buildingA; linked.UnitId = unitA; linked.AutoRenew = true; linked.RenewalMonths = 12;
var linkedId = await admin.CreateAsync(a, linked);
Assert((await owner.GetAsync(a, linkedId)).UnitId == unitA, "valid optional property and renewal references");
var current = await owner.GetAsync(a, idA);
await Expect<AgreementValidationException>(() => owner.SetAccessAsync(a, idA, foreignId, AgreementAccessLevel.Read, current.Revision), "foreign grant rejected");
await owner.SetAccessAsync(a, idA, readerId, AgreementAccessLevel.Read, current.Revision);
current = await owner.GetAsync(a, idA);
await owner.SetAccessAsync(a, idA, editorId, AgreementAccessLevel.Edit, current.Revision);
var read = await reader.GetAsync(a, idA);
Assert(!read.CanEdit && !read.CanManageAccess, "read permissions");
await Expect<UnauthorizedAccessException>(() => reader.UpdateAsync(a, idA, read), "reader cannot edit");
await Expect<UnauthorizedAccessException>(() => reader.UploadAsync(a, idA, read.Revision, "contract.pdf", Pdf(), AgreementDocumentCategory.Contract, null), "reader cannot upload");
var edit = await editor.GetAsync(a, idA);
Assert(edit.CanEdit && !edit.CanManageAccess, "editor permissions");
await Expect<UnauthorizedAccessException>(() => editor.SetAccessAsync(a, idA, strangerId, AgreementAccessLevel.Read, edit.Revision), "editor cannot grant");
edit.OwnerUserId = editorId;
await Expect<UnauthorizedAccessException>(() => editor.UpdateAsync(a, idA, edit), "editor cannot change owner");
edit.OwnerUserId = ownerId; edit.Title = "Updated title";
await editor.UpdateAsync(a, idA, edit);
await Expect<AgreementValidationException>(() => owner.UpdateAsync(a, idA, edit), "stale edit detected");
current = await owner.GetAsync(a, idA);
var concurrent = await Task.WhenAll(TryEdit(current), TryEdit(current));
Assert(concurrent.Count(x => x) == 1, "only one concurrent edit succeeds");
current = await owner.GetAsync(a, idA);
await editor.UploadAsync(a, idA, current.Revision, "../../contract.pdf", Pdf(), AgreementDocumentCategory.Contract, "Signed copy");
current = await owner.GetAsync(a, idA);
var doc = current.Documents.Single();
Assert(doc.FileName == "contract.pdf", "safe filename excludes path");
var download = await reader.DownloadAsync(a, doc.Id);
await using (download.Content) { Assert(download.MediaType == "application/pdf" && download.Content.Length > 0, "reader downloads authorised document"); }
await Expect<UnauthorizedAccessException>(() => stranger.DownloadAsync(a, doc.Id), "ungranted document download denied");
await Expect<UnauthorizedAccessException>(() => foreign.DownloadAsync(b, doc.Id), "cross-account document download denied");
await Expect<UnauthorizedAccessException>(() => admin.DownloadAsync(b, doc.Id), "forged account download denied");
await editor.UploadAsync(a, idA, current.Revision, "contract.pdf", Pdf(), AgreementDocumentCategory.Attachment, null);
current = await owner.GetAsync(a, idA);
Assert(current.Documents.Count == 2 && current.Documents.Select(x => x.Id).Distinct().Count() == 2, "same filename creates separate documents");
Assert((await admin.ListAsync(a, new() { Search = "COUNTERPARTY A" })).TotalCount == 3, "case-insensitive counterparty search");
Assert((await reader.ListAsync(a, new())).TotalCount == 1, "grantee sees only shared agreement");
Assert((await admin.ListAsync(a, new() { OwnerUserId = foreignId })).TotalCount == 0, "owner filter does not widen tenant scope");
await using (var db = factory.CreateDbContext())
{
    db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.AccountId == a && x.UserId == readerId));
    await db.SaveChangesAsync();
}
await Expect<UnauthorizedAccessException>(() => reader.GetAsync(a, idA), "former member loses stale grant access");
await Expect<UnauthorizedAccessException>(() => reader.DownloadAsync(a, doc.Id), "former member cannot download");
Assert(!await new TenantAuthorizationService(factory, new TestUserContext(readerId, a)).CanUseAgreementsAsync(), "former member menu denied");

await Expect<AgreementFileException>(() => storage.StoreAsync(Pdf(), "file.exe"), "extension rejected");
await Expect<AgreementFileException>(() => storage.StoreAsync(new MemoryStream("not a PDF"u8.ToArray()), "file.pdf"), "fake PDF content rejected");
await Expect<AgreementFileException>(() => storage.StoreAsync(Pdf(), "file.png"), "mismatched extension/content rejected");
await Expect<AgreementFileException>(() => storage.StoreAsync(new MemoryStream(new byte[1025]), "file.pdf"), "actual streamed size enforced");
await Expect<AgreementFileException>(() => storage.StoreAsync(new MemoryStream(), "file.pdf"), "empty file rejected");
await Expect<AgreementFileException>(() => storage.StoreAsync(Office(false), "file.xlsx"), "DOCX disguised as XLSX rejected");
var docx = await storage.StoreAsync(Office(false), "file.docx");
Assert(docx.MediaType.EndsWith("wordprocessingml.document"), "DOCX package accepted");
var xlsx = await storage.StoreAsync(Office(true), "file.xlsx");
Assert(xlsx.MediaType.EndsWith("spreadsheetml.sheet"), "XLSX package accepted");
var png = await storage.StoreAsync(new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")), "image.png");
Assert(png.MediaType == "image/png", "PNG signature accepted");
var fileCount = Directory.GetFiles(root).Length;
await Expect<OperationCanceledException>(() => storage.StoreAsync(new InterruptedStream(), "file.pdf"), "interrupted upload fails");
Assert(Directory.GetFiles(root).Length == fileCount && !Directory.GetFiles(root, "*.partial").Any(), "interrupted upload cleans temporary files");

// Change the agreement after streaming begins, forcing metadata save to reject its revision.
var before = await owner.GetAsync(a, idA);
await Expect<AgreementValidationException>(() => editor.UploadAsync(a, idA, before.Revision, "file.pdf",
    new CallbackStream(Pdf().ToArray(), async () => {
        var latest = await owner.GetAsync(a, idA); latest.Title = "Changed during upload";
        await owner.UpdateAsync(a, idA, latest);
    }), AgreementDocumentCategory.Contract, null), "upload rechecks revision after streaming");
Assert(Directory.GetFiles(root).Length == fileCount, "failed metadata save discards uncommitted file");
Assert((await owner.GetAsync(a, idA)).Documents.Count == 2, "failed upload leaves no document metadata");
await using (var db = factory.CreateDbContext())
{
    var target = await db.Organizations.SingleAsync(x => x.AccountId == a && x.Id == orgA);
    db.Organizations.Remove(target);
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "database protects referenced counterparties");
}
await using (var db = factory.CreateDbContext())
{
    db.AgreementAccess.Add(new AgreementAccess { Id = Guid.NewGuid(), AccountId = b, AgreementId = idA, UserId = foreignId, Level = AgreementAccessLevel.Read });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "database rejects cross-account agreement grant");
}
// Generic classification replaces the former dedicated hierarchy/FK scenarios.
var adminContext = new TestUserContext(adminId, a);
var dimensions = new DimensionService(factory, adminContext, new TenantAuthorizationService(factory, adminContext), TimeProvider.System);
var dimensionId = await dimensions.SaveDimensionAsync(null, new() { Name = "Classification", Code = "CLASS", LeafOnly = false });
var rootValue = await dimensions.SaveValueAsync(dimensionId, null, new() { DimensionId = dimensionId, Name = "Operations", Code = "ROOT" });
var childValue = await dimensions.SaveValueAsync(dimensionId, null, new() { DimensionId = dimensionId, ParentId = rootValue, Name = "IT", Code = "CHILD" });
var foreignDimension = Guid.NewGuid(); var foreignValue = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Dimensions.Add(new Dimension { Id = foreignDimension, AccountId = b, Name = "Foreign", Code = "FOREIGN" });
    db.DimensionValues.Add(new DimensionValue { Id = foreignValue, AccountId = b, DimensionId = foreignDimension, Name = "Foreign value", Code = "FOREIGN" });
    await db.SaveChangesAsync();
}
await admin.SaveAgreementDimensionRuleAsync(a, new() { DimensionId = dimensionId, IsEnabled = true });
var choices = await admin.GetOptionsAsync(a);
Assert(choices.Dimensions.Single().Values.Single(x => x.Id == childValue).Path == "Operations / IT", "generic options retain complete hierarchy path");
Assert(!choices.Dimensions.Any(x => x.DimensionId == foreignDimension), "generic options exclude foreign account dimensions");
var classifiedRequest = Request(); classifiedRequest.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [childValue] }];
var classifiedId = await admin.CreateAsync(a, classifiedRequest);
var classified = await owner.GetAsync(a, classifiedId);
Assert(classified.Dimensions.Single().Values.Single(x => x.IsAssigned).Id == childValue, "generic create/detail round-trip explicit value");
await using (var db = factory.CreateDbContext())
{
    db.DimensionValues.Remove(await db.DimensionValues.SingleAsync(x => x.Id == childValue));
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "referenced dimension value is protected by Restrict instead of obsolete runtime deletion API");
}
await Invalid(r => r.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [foreignValue] }], "foreign dimension value rejected");
await Invalid(r => r.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [Guid.NewGuid()] }], "missing dimension value rejected");
await Expect<UnauthorizedAccessException>(() => stranger.GetOptionsAsync(a, classifiedId), "ungranted user cannot read classification edit options");
Assert((await owner.GetOptionsAsync(a, classifiedId)).Dimensions.Count == 1, "owner can classify without register administration");
var node = (await dimensions.GetAsync()).Values.Single(x => x.Id == childValue); node.IsActive = false;
await dimensions.SaveValueAsync(dimensionId, childValue, node);
Assert(!(await admin.GetOptionsAsync(a)).Dimensions.Single().Values.Any(x => x.Id == childValue), "new agreement options exclude inactive value");
Assert((await owner.GetOptionsAsync(a, classifiedId)).Dimensions.Single().Values.Single(x => x.Id == childValue).IsHistorical, "assigned inactive value remains visible for editing");
classified.Title = "Retain historical choices"; await owner.UpdateAsync(a, classifiedId, classified);
Assert((await owner.GetAsync(a, classifiedId)).Dimensions.Single().Values.Any(x => x.Id == childValue && x.IsAssigned), "unrelated edit preserves historical choice");
await Invalid(r => r.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [childValue] }], "new inactive choice rejected");
classified = await owner.GetAsync(a, classifiedId); classified.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [rootValue] }];
await owner.UpdateAsync(a, classifiedId, classified);
classified = await owner.GetAsync(a, classifiedId); classified.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [foreignValue] }];
await Expect<AgreementValidationException>(() => owner.UpdateAsync(a, classifiedId, classified), "foreign edit value rejected");
classified.DimensionChanges = [new() { DimensionId = dimensionId, ValueIds = [] }]; await owner.UpdateAsync(a, classifiedId, classified);
Assert(!(await owner.GetAsync(a, classifiedId)).Dimensions.SelectMany(x => x.Values).Any(x => x.IsAssigned), "explicit empty patch clears classification");
await using (var db = factory.CreateDbContext())
{
    db.AgreementDimensionAssignments.Add(new AgreementDimensionAssignment { Id = Guid.NewGuid(), AccountId = a, AgreementId = classifiedId, DimensionId = dimensionId, ValueId = foreignValue });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync(), "database rejects cross-account/wrong-dimension assignment");
}

Console.WriteLine("All agreement smoke tests passed.");

AgreementService Service(Guid userId, Guid accountId)
{
    var context = new TestUserContext(userId, accountId);
    return new(factory, context, new TenantAuthorizationService(factory, context), storage, NullLogger<AgreementService>.Instance);
}
SaveAgreementRequest Request() => new() { Title = "Agreement", CounterpartyOrganizationId = orgA, OwnerUserId = ownerId, StartDate = new(2030, 1, 1), Form = AgreementForm.Renewing, RenewalDate = new(2031, 1, 1) };
async Task Invalid(Action<SaveAgreementRequest> change, string name)
{
    var request = Request(); change(request);
    await Expect<AgreementValidationException>(() => admin.CreateAsync(a, request), name);
}
async Task<bool> TryEdit(AgreementDetailsDto request)
{
    try { await Service(ownerId, a).UpdateAsync(a, idA, request); return true; }
    catch (AgreementValidationException ex) when (ex.Message == "AgreementConcurrencyConflict") { return false; }
}
static MemoryStream Pdf() => new("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray());
static MemoryStream Office(bool excel)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
    {
        var main = excel ? "xl/workbook.xml" : "word/document.xml";
        var type = excel ? "spreadsheetml.sheet" : "wordprocessingml.document";
        using (var writer = new StreamWriter(zip.CreateEntry(main).Open())) writer.Write("<document/>");
        using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
            writer.Write($"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{main}\" ContentType=\"application/vnd.openxmlformats-officedocument.{type}.main+xml\"/></Types>");
    }
    stream.Position = 0; return stream;
}
static void Assert(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
}
static async Task Expect<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine($"PASS: {name}"); return; }
    throw new Exception($"FAIL: {name}: expected {typeof(T).Name}");
}
sealed class TestFactory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{
    public TenantPlatformDbContext CreateDbContext() => new(options);
}
sealed class TestUserContext(Guid userId, Guid accountId, bool platform = false) : ICurrentUserContextService
{
    public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = userId, CurrentAccountId = accountId, IsPlatformAdmin = platform };
}
sealed class InterruptedStream : MemoryStream
{
    private bool started;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (started) throw new OperationCanceledException();
        started = true; buffer.Span[0] = 37; return ValueTask.FromResult(1);
    }
}
sealed class CallbackStream(byte[] bytes, Func<Task> callback) : MemoryStream(bytes)
{
    private bool called;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!called) { called = true; await callback(); }
        return await base.ReadAsync(buffer, cancellationToken);
    }
}

