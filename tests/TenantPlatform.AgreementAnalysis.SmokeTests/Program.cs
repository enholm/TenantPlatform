using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

var connection = Environment.GetEnvironmentVariable("AGREEMENT_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set AGREEMENT_TEST_CONNECTION to disposable PostgreSQL database tenant_agreement_tests.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_agreement_tests") throw new InvalidOperationException("Use only tenant_agreement_tests.");
var schema = "analysis_test_" + Guid.NewGuid().ToString("N");
await using var setup = new NpgsqlConnection(connection);
await setup.OpenAsync();
await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
cs.SearchPath = schema;
var factory = new TestFactory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString)
    .AddInterceptors(new AuditSaveChangesInterceptor(new TestAuditContext())).Options);
var root = Path.Combine(Path.GetTempPath(), schema);
var storage = new TestStorage(new LocalAgreementDocumentStorage(Options.Create(new AgreementDocumentStorageOptions { RootPath = root, MaxFileSizeBytes = 1024 * 1024 })));
var handler = new MockOpenAi();
var client = new OpenAiContractAnalysisClient(new HttpClient(handler), Options.Create(new AgreementAnalysisOptions { ApiKey = "mock-only-key", Model = "mock-model" }), storage);
var clock = new TestClock();
var account = Guid.NewGuid(); var otherAccount = Guid.NewGuid();
var adminId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var readerId = Guid.NewGuid(); var foreignId = Guid.NewGuid();
var organizationId = Guid.NewGuid(); var foreignOrg = Guid.NewGuid();
try
{
    await using (var db = factory.CreateDbContext())
    {
        Assert(!db.Database.HasPendingModelChanges(), "migration snapshot matches model");
        await db.Database.MigrateAsync();
        Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies");
        db.Accounts.AddRange(new Account { Id = account, Name = "Our Business AS" }, new Account { Id = otherAccount, Name = "Foreign Business" });
        foreach (var userId in new[] { adminId, ownerId, readerId, foreignId })
        {
            db.Users.Add(new User { Id = userId, FirstName = "Synthetic", Email = userId + "@example.test" });
            var membership = new UserAccount { Id = Guid.NewGuid(), UserId = userId, AccountId = userId == foreignId ? otherAccount : account };
            db.UserAccounts.Add(membership);
            if (userId == adminId || userId == foreignId)
                db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
        }
        db.Organizations.AddRange(new Organization { Id = organizationId, AccountId = account, Name = "Supplier AS", OrganizationNumber = "123 456 789" },
            new Organization { Id = foreignOrg, AccountId = otherAccount, Name = "Foreign only", OrganizationNumber = "999999999" });
        await db.SaveChangesAsync();
    }
    var admin = Service(adminId, account); var owner = Service(ownerId, account); var foreign = Service(foreignId, otherAccount);
    await Expect<UnauthorizedAccessException>(() => owner.StartAnalysisDraftAsync(account, null), "ordinary member cannot create agreement draft");
    await Expect<UnauthorizedAccessException>(() => admin.StartAnalysisDraftAsync(otherAccount, null), "forged tenant denied");
    var draft = await DraftAsync(attachments: 2);
    Assert(draft.Files.Count == 3, "main contract and two attachments stored before analysis");
    await Expect<AgreementValidationException>(() => admin.AddAnalysisFileAsync(account, draft.Id, "extra.pdf", Pdf(), AgreementDocumentCategory.Contract), "one main contract enforced");
    await Expect<AgreementValidationException>(() => admin.AddAnalysisFileAsync(account, draft.Id, "bad.docx", Pdf(), AgreementDocumentCategory.Attachment), "unsupported type denied");
    await Expect<AgreementFileException>(() => admin.AddAnalysisFileAsync(account, draft.Id, "fake.pdf", new MemoryStream("fake"u8.ToArray()), AgreementDocumentCategory.Attachment), "file signature validated");
    await Expect<UnauthorizedAccessException>(() => owner.AnalyzeAsync(account, draft.Id), "other user cannot analyze draft");
    await Expect<UnauthorizedAccessException>(() => foreign.DownloadAnalysisFileAsync(otherAccount, draft.Id, draft.Files[0].Id), "foreign draft download denied");
    await Expect<UnauthorizedAccessException>(() => admin.DownloadAnalysisFileAsync(account, draft.Id, Guid.NewGuid()), "foreign file ID denied");
    using (var file = (await admin.DownloadAnalysisFileAsync(account, draft.Id, draft.Files[0].Id)).Content)
        Assert(file.Length > 0, "draft source is readable by owner");
    draft = await admin.AnalyzeAsync(account, draft.Id);
    Assert(handler.LastFileCount == 3 && handler.OwnBusiness == "Our Business AS", "all files and actual account identity sent together");
    Assert(handler.ValidRequest, "strict schema, inline PDF, store:false, no tools, server authorization");
    Assert(draft.Result!.Findings.Count(x => x.Category == AgreementFindingCategory.Liability) == 2 && draft.Result.Findings[1].Sources.Count == 2,
        "multiple findings and sources retained");
    Assert(draft.Result.Findings.Any(x => x.Status == AgreementFindingStatus.NotFound) && draft.Result.Findings.Any(x => x.Status == AgreementFindingStatus.Unclear), "missing and conflicting clauses visible");
    var calls = handler.Calls;
    await admin.AnalyzeAsync(account, draft.Id);
    Assert(handler.Calls == calls, "repeated analyze on ready result avoids repeat cost");
    var matches = await admin.FindAnalysisOrganizationsAsync(account, draft.Id, "wrong name", "123456789");
    Assert(matches.Single().Id == organizationId, "organisation number matched first with formatting normalization");
    Assert((await admin.FindAnalysisOrganizationsAsync(account, draft.Id, " supplier   as ", "")).Single().Id == organizationId, "exact normalized legal name matched");
    Assert((await admin.FindAnalysisOrganizationsAsync(account, draft.Id, "Supplier A", "")).Count == 0, "fuzzy names not matched");
    Assert((await admin.FindAnalysisOrganizationsAsync(account, draft.Id, "Foreign only", "999999999")).Count == 0, "organization matching tenant isolated");
    var approval = Approval(draft, organizationId);
    approval.Findings[1].Value = "User corrected liability summary";
    approval.Findings[1].Status = AgreementFindingStatus.Unclear;
    await Expect<UnauthorizedAccessException>(() => admin.ApproveAnalysisAsync(account, draft.Id, Approval(draft, foreignOrg)), "foreign organization denied on approval");
    var agreementId = await admin.ApproveAnalysisAsync(account, draft.Id, approval);
    Assert(await admin.ApproveAnalysisAsync(account, draft.Id, approval) == agreementId, "repeat approval returns same agreement");
    var history = await admin.GetAnalysesAsync(account, agreementId);
    var saved = history.Single();
    Assert(saved.Files.Count == 3 && saved.Files.All(x => x.AgreementDocumentId.HasValue), "all documents promoted to archive");
    var corrected = saved.Findings.Single(x => x.Position == 1);
    Assert(corrected.AdjustedValue == approval.Findings[1].Value && corrected.OriginalValue == draft.Result.Findings[1].Value && corrected.Sources.Any(x => x.Quote == "Original source quotation"),
        "original values and quotations survive corrections");
    Assert(saved.ApprovedByUserId == adminId && saved.ApprovedUtc.HasValue && saved.Model == "mock-model-2026" && saved.SchemaVersion == "contract-v1", "approval provenance stored");
    await Expect<UnauthorizedAccessException>(() => foreign.GetAnalysesAsync(otherAccount, agreementId), "approved history tenant isolated");
    var details = await admin.GetAsync(account, agreementId);
    await admin.SetAccessAsync(account, agreementId, readerId, AgreementAccessLevel.Read, details.Revision);
    var reader = Service(readerId, account);
    Assert((await reader.GetAnalysesAsync(account, agreementId)).Count == 1, "read grant can view findings");
    await Expect<UnauthorizedAccessException>(() => reader.StartAnalysisDraftAsync(account, agreementId), "read grant cannot analyze");

    // Owner reanalysis appends a version and keeps old documents and approved findings.
    var version = await DraftAsync(agreementId: agreementId, service: owner);
    version = await owner.AnalyzeAsync(account, version.Id);
    await owner.ApproveAnalysisAsync(account, version.Id, Approval(version, organizationId));
    Assert((await owner.GetAnalysesAsync(account, agreementId)).Count == 2 && (await owner.GetAsync(account, agreementId)).Documents.Count == 4, "reanalysis appends version without overwrite");
    var stale = await DraftAsync(agreementId: agreementId);
    stale = await admin.AnalyzeAsync(account, stale.Id);
    details = await admin.GetAsync(account, agreementId); details.Title = "Changed after draft";
    await admin.UpdateAsync(account, agreementId, details);
    await Expect<AgreementValidationException>(() => admin.ApproveAnalysisAsync(account, stale.Id, Approval(stale, organizationId)), "stale agreement revision cannot silently replace link");
    await admin.DiscardAnalysisAsync(account, stale.Id);

    // Discard leaves usage but neither agreement nor organization and physically removes files.
    var discarded = await DraftAsync(); discarded = await admin.AnalyzeAsync(account, discarded.Id);
    var before = await Counts();
    string[] keys;
    await using (var db = factory.CreateDbContext()) keys = await db.AgreementAnalysisFiles.Where(x => x.AnalysisId == discarded.Id).Select(x => x.StorageKey).ToArrayAsync();
    await admin.DiscardAnalysisAsync(account, discarded.Id); await admin.DiscardAnalysisAsync(account, discarded.Id);
    Assert(await Counts() == before && keys.All(k => !File.Exists(Path.Combine(root, k))), "discard deletes temporary files without creating business records");
    await using (var db = factory.CreateDbContext())
    {
        Assert(!await db.AgreementAnalyses.AnyAsync(x => x.Id == discarded.Id), "discard deletes analysis content");
        var usage = await db.AgreementAiUsage.SingleAsync(x => x.AnalysisId == discarded.Id);
        Assert(usage.InputTokens == 123 && usage.CachedInputTokens == 23 && usage.OutputTokens == 45 && usage.ResponseId is not null && usage.UserId == adminId && usage.AccountId == account,
            "actual usage retained after discard");
        Assert(!(await db.AuditLogs.Select(x => x.ChangesJson).ToListAsync()).Any(x => x!.Contains("Original source quotation") || x.Contains("User corrected liability summary")), "audit log excludes analysis content and citations");
    }
    var newDraft = await DraftAsync(); newDraft = await admin.AnalyzeAsync(account, newDraft.Id);
    var newApproval = Approval(newDraft, null); newApproval.CounterpartyName = "New synthetic supplier"; newApproval.OrganizationNumber = "555555555";
    var two = await Task.WhenAll(admin.ApproveAnalysisAsync(account, newDraft.Id, newApproval), Service(adminId, account).ApproveAnalysisAsync(account, newDraft.Id, newApproval));
    Assert(two[0] == two[1], "simultaneous double click creates exactly one agreement");
    await using (var db = factory.CreateDbContext()) Assert(await db.Organizations.CountAsync(x => x.AccountId == account && x.OrganizationNumber == "555555555") == 1, "new organization created exactly once on approval");
    // Different drafts racing to create the same counterparty require the loser to review the new match.
    var race1 = await DraftAsync(); race1 = await admin.AnalyzeAsync(account, race1.Id);
    var race2 = await DraftAsync(); race2 = await admin.AnalyzeAsync(account, race2.Id);
    var r1 = Approval(race1, null); r1.CounterpartyName = "Concurrent supplier"; r1.OrganizationNumber = "444444444";
    var r2 = Approval(race2, null); r2.CounterpartyName = r1.CounterpartyName; r2.OrganizationNumber = r1.OrganizationNumber;
    var racing = await Task.WhenAll(ApproveRace(race1.Id, r1), ApproveRace(race2.Id, r2));
    Assert(racing.Count(x => x) == 1, "concurrent drafts cannot create duplicate organization");
    await using (var db = factory.CreateDbContext()) Assert(await db.Organizations.CountAsync(x => x.AccountId == account && x.OrganizationNumber == "444444444") == 1, "concurrent counterparty insert serialized");
    // Existing ambiguous matches always need an explicit selection.
    await using (var db = factory.CreateDbContext()) { db.Organizations.Add(new Organization { Id = Guid.NewGuid(), AccountId = account, Name = "Supplier AS" }); await db.SaveChangesAsync(); }
    var ambiguous = await DraftAsync(); ambiguous = await admin.AnalyzeAsync(account, ambiguous.Id);
    Assert((await admin.FindAnalysisOrganizationsAsync(account, ambiguous.Id, "Supplier AS", "")).Count == 2, "multiple exact names returned for user choice");
    await Expect<AgreementValidationException>(() => admin.ApproveAnalysisAsync(account, ambiguous.Id, Approval(ambiguous, null)), "automatic creation blocked when organization already matches");
    var noConfirm = Approval(ambiguous, organizationId); noConfirm.CounterpartyConfirmed = false;
    await Expect<AgreementValidationException>(() => admin.ApproveAnalysisAsync(account, ambiguous.Id, noConfirm), "identity review required server side");
    await admin.DiscardAnalysisAsync(account, ambiguous.Id);
    // Missing required agreement fields roll back organization creation too.
    var invalid = await DraftAsync(); invalid = await admin.AnalyzeAsync(account, invalid.Id);
    var invalidApproval = Approval(invalid, null); invalidApproval.CounterpartyName = "Rolled back organization"; invalidApproval.OrganizationNumber = ""; invalidApproval.Agreement.Title = "";
    before = await Counts();
    await Expect<AgreementValidationException>(() => admin.ApproveAnalysisAsync(account, invalid.Id, invalidApproval), "existing mandatory agreement validation reused");
    Assert(await Counts() == before, "partial approval rolls back all related inserts");

    foreach (var mode in new[] { "http", "invalid", "incomplete", "refusal", "network" })
    {
        var failed = await DraftAsync(); handler.Mode = mode;
        await Expect<Exception>(() => admin.AnalyzeAsync(account, failed.Id), mode + " response fails safely");
        await using (var db = factory.CreateDbContext())
        {
            var state = await db.AgreementAnalyses.SingleAsync(x => x.Id == failed.Id);
            Assert(state.State == AgreementAnalysisState.Draft && await db.AgreementAnalysisFiles.AnyAsync(x => x.AnalysisId == failed.Id), mode + " preserves draft for retry");
            Assert(await db.AgreementAiUsage.AnyAsync(x => x.AnalysisId == failed.Id), mode + " records call status");
        }
        handler.Mode = "ok"; failed = await admin.AnalyzeAsync(account, failed.Id);
        Assert(failed.Result is not null, mode + " can retry");
        await admin.DiscardAnalysisAsync(account, failed.Id);
    }
    handler.Mode = "unreadable";
    var unreadable = await DraftAsync(attachments: 1); unreadable = await admin.AnalyzeAsync(account, unreadable.Id);
    Assert(unreadable.Result!.Documents.Count(x => !x.Processed) == 1, "unreadable file explicitly reported");
    await Expect<AgreementValidationException>(() => admin.ApproveAnalysisAsync(account, unreadable.Id, Approval(unreadable, organizationId)), "partial analysis cannot be approved");
    handler.Mode = "ambiguous";
    var partyDraft = await DraftAsync(); partyDraft = await admin.AnalyzeAsync(account, partyDraft.Id);
    Assert(partyDraft.Result!.Counterparty.RequiresClarification && partyDraft.Result.Counterparty.Name == "", "uncertain own business/counterparty is not guessed");
    handler.Mode = "ok";
    var incompleteSources = JsonSerializer.Serialize(draft.Result with { Findings = draft.Result.Findings.Where(x => x.Category != AgreementFindingCategory.Payment).ToList() }, ContractAnalysisJson.Options);
    await Expect<AgreementValidationException>(() => Task.FromResult(ContractAnalysisSchema.Parse(incompleteSources, draft.Files.Select(x => x.Id).ToArray())), "missing category rejected");
    var badSource = draft.Result.Findings[1] with { Sources = [new(Guid.NewGuid(), "Invented source", "", null)] };
    var badResult = draft.Result with { Findings = [badSource, ..draft.Result.Findings.Where(x => x.Category != AgreementFindingCategory.Liability)] };
    await Expect<AgreementValidationException>(() => Task.FromResult(ContractAnalysisSchema.Parse(JsonSerializer.Serialize(badResult, ContractAnalysisJson.Options), draft.Files.Select(x => x.Id).ToArray())), "foreign citation ID rejected");

    // An in-flight lease prevents duplicate calls, deletion and approval.
    var leased = await DraftAsync();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    handler.BeforeReply = async token => { entered.SetResult(); await release.Task.WaitAsync(token); };
    var inFlight = admin.AnalyzeAsync(account, leased.Id);
    await entered.Task;
    await Expect<AgreementValidationException>(() => admin.AnalyzeAsync(account, leased.Id), "in-flight analysis cannot be duplicated");
    await Expect<AgreementValidationException>(() => admin.DiscardAnalysisAsync(account, leased.Id), "in-flight files cannot be deleted");
    release.SetResult(); leased = await inFlight; handler.BeforeReply = null;
    await admin.DiscardAnalysisAsync(account, leased.Id);
    var revoked = await DraftAsync();
    handler.BeforeReply = async _ =>
    {
        await using var db = factory.CreateDbContext();
        await db.Users.Where(x => x.Id == adminId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
    };
    await Expect<UnauthorizedAccessException>(() => admin.AnalyzeAsync(account, revoked.Id), "access rechecked after API call");
    handler.BeforeReply = null;
    await using (var db = factory.CreateDbContext())
    {
        Assert((await db.AgreementAiUsage.SingleAsync(x => x.AnalysisId == revoked.Id)).InputTokens == 123, "usage retained when access revoked during API call");
        await db.Users.Where(x => x.Id == adminId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
    }
    await admin.DiscardAnalysisAsync(account, revoked.Id);
    var removing = await DraftAsync(attachments: 1); removing = await admin.AnalyzeAsync(account, removing.Id);
    var attachmentId = removing.Files.Single(x => x.Category == AgreementDocumentCategory.Attachment).Id;
    removing = await admin.RemoveAnalysisFileAsync(account, removing.Id, attachmentId);
    Assert(removing.Files.Count == 1 && removing.Result is null, "removing a file invalidates analysis and retains the other file");
    await admin.DiscardAnalysisAsync(account, removing.Id);
    var limited = await DraftAsync(attachments: 9);
    await Expect<AgreementValidationException>(() => admin.AddAnalysisFileAsync(account, limited.Id, "eleventh.pdf", Pdf(), AgreementDocumentCategory.Attachment), "total file count enforced server side");
    await admin.DiscardAnalysisAsync(account, limited.Id);

    var pending = await DraftAsync(attachments: 1);
    storage.FailDeletes = true;
    await Expect<IOException>(() => admin.DiscardAnalysisAsync(account, pending.Id), "file deletion failure surfaces and is retryable");
    storage.FailDeletes = false;
    await new AgreementAnalysisCleanup(factory, storage, clock).RunAsync(default);
    await using (var db = factory.CreateDbContext()) Assert(!await db.AgreementAnalyses.AnyAsync(x => x.Id == pending.Id), "worker retries interrupted file cleanup");
    var expiring = await DraftAsync();
    clock.Advance(TimeSpan.FromHours(25));
    var cleanup = new AgreementAnalysisCleanup(factory, storage, clock);
    await cleanup.RunAsync(default);
    await using (var db = factory.CreateDbContext()) Assert(!await db.AgreementAnalyses.AnyAsync(x => x.Id == expiring.Id) && await db.AgreementAnalyses.AnyAsync(x => x.Id == saved.Id), "expiry cleans abandoned drafts and retains approved history");
    var orphan = Guid.NewGuid().ToString("N"); await File.WriteAllTextAsync(Path.Combine(root, orphan), "synthetic orphan");
    File.SetLastWriteTimeUtc(Path.Combine(root, orphan), clock.GetUtcNow().AddDays(-3).UtcDateTime);
    await cleanup.CleanOrphansAsync(root, default);
    Assert(!File.Exists(Path.Combine(root, orphan)) && saved.Files.All(x => File.Exists(Path.Combine(root, x.StorageKey))), "orphan recovery protects archive files");
    Console.WriteLine("All contract analysis smoke tests passed.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup);
    await drop.ExecuteNonQueryAsync();
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
AgreementService Service(Guid user, Guid tenant)
{
    var context = new TestUserContext(user, tenant);
    return new(factory, context, new TenantAuthorizationService(factory, context), storage, NullLogger<AgreementService>.Instance, clock, analysisClient: client);
}
async Task<AnalysisDraftDto> DraftAsync(int attachments = 0, Guid? agreementId = null, AgreementService? service = null)
{
    service ??= Service(adminId, account);
    var d = await service.StartAnalysisDraftAsync(account, agreementId);
    d = await service.AddAnalysisFileAsync(account, d.Id, "main.pdf", Pdf(), AgreementDocumentCategory.Contract);
    for (var i = 0; i < attachments; i++) d = await service.AddAnalysisFileAsync(account, d.Id, "attachment.pdf", Pdf(), AgreementDocumentCategory.Attachment);
    return d;
}
ApproveAnalysisRequest Approval(AnalysisDraftDto d, Guid? org) => new()
{
    OrganizationId = org, CounterpartyName = "Supplier AS", OrganizationNumber = "123456789", Address = "Synthetic street 1", CounterpartyConfirmed = true,
    Agreement = new() { Title = "Synthetic contract", OwnerUserId = ownerId, StartDate = new(2030, 1, 1), EndDate = new(2031, 1, 1), Form = AgreementForm.FixedTerm },
    Findings = d.Result!.Findings.Select(x => new AnalysisFindingCorrection { Value = x.Value, Status = x.Status, Parties = x.Parties }).ToList()
};
async Task<(int, int)> Counts() { await using var db = factory.CreateDbContext(); return (await db.Agreements.CountAsync(), await db.Organizations.CountAsync()); }
async Task<bool> ApproveRace(Guid d, ApproveAnalysisRequest r)
{
    try { await Service(adminId, account).ApproveAnalysisAsync(account, d, r); return true; }
    catch (AgreementValidationException ex) when (ex.Message == "AnalysisOrganizationChanged") { return false; }
}
static MemoryStream Pdf() => new("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray());
static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
static async Task Expect<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL: " + message);
}
sealed class TestFactory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{ public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class TestUserContext(Guid user, Guid account) : ICurrentUserContextService
{ public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account }; }
sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
sealed class TestAuditContext : IAuditUserContext
{
    public Guid? AccountId => null;
    public Guid? UserId => null;
    public string? Email => null;
}
sealed class TestStorage(IAgreementDocumentStorage inner) : IAgreementDocumentStorage
{
    public bool FailDeletes { get; set; }
    public long MaxFileSizeBytes => inner.MaxFileSizeBytes;
    public Task<StoredAgreementFile> StoreAsync(Stream source, string name, CancellationToken ct = default) => inner.StoreAsync(source, name, ct);
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => inner.OpenReadAsync(key, ct);
    public Task DiscardUncommittedAsync(string key) => FailDeletes ? throw new IOException("Synthetic deletion failure") : inner.DiscardUncommittedAsync(key);
}
sealed class MockOpenAi : HttpMessageHandler
{
    public string Mode { get; set; } = "ok";
    public Func<CancellationToken, Task>? BeforeReply { get; set; }
    public int Calls { get; private set; }
    public int LastFileCount { get; private set; }
    public string OwnBusiness { get; private set; } = "";
    public bool ValidRequest { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var root = input.RootElement;
        var content = root.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToArray();
        OwnBusiness = content[0].GetProperty("text").GetString()!.Split(": ", 2)[1];
        var ids = content.Where(x => x.GetProperty("type").GetString() == "input_file").Select(x => Guid.Parse(Path.GetFileNameWithoutExtension(x.GetProperty("filename").GetString())!)).ToArray();
        LastFileCount = ids.Length;
        ValidRequest = request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses" && request.Headers.Authorization?.Parameter == "mock-only-key" &&
            !root.GetProperty("store").GetBoolean() && root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean() && !root.TryGetProperty("tools", out _) &&
            content.Where(x => x.GetProperty("type").GetString() == "input_file").All(x => x.GetProperty("file_data").GetString()!.StartsWith("data:application/pdf;base64,"));
        if (BeforeReply is not null) await BeforeReply(ct);
        if (Mode == "http") return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("SENSITIVE provider body never logged") };
        if (Mode == "network") throw new HttpRequestException("Synthetic failure");
        var findings = Enum.GetValues<AgreementFindingCategory>().Select(c => new AnalysisFinding(c, AgreementFindingStatus.NotFound, "Ikke funnet", "", "", [])).ToList();
        findings[1] = new(AgreementFindingCategory.Liability, AgreementFindingStatus.Found, "Leverandøren er ansvarlig", "Supplier AS", "",
            [new(ids[0], "Original source quotation", "Liability", 1), new(ids[^1], "Second original quotation", "Schedule", null)]);
        findings.Add(new(AgreementFindingCategory.Liability, AgreementFindingStatus.Unclear, "Motstridende vilkår", "Both parties", "Kontrakten og vedlegget er i konflikt", [new(ids[^1], "Conflicting clause", "", null)]));
        var result = new ContractAnalysisResult(new(Mode == "ambiguous" ? "" : "Supplier AS", "123456789", "Synthetic street 1", Mode == "ambiguous", Mode == "ambiguous" ? "Flere mulige parter" : ""),
            ["Our Business AS", "Supplier AS"], ids.Select((id, i) => new AnalysisDocumentResult(id, Mode != "unreadable" || i != ids.Length - 1, Mode == "unreadable" && i == ids.Length - 1 ? "Uleselig vedlegg" : "")).ToList(), findings, ["Motstrid må kontrolleres"]);
        var text = Mode == "invalid" ? "{\"incomplete\":true}" : JsonSerializer.Serialize(result, ContractAnalysisJson.Options);
        object part = Mode == "refusal" ? new { type = "refusal", refusal = "mock refusal" } : new { type = "output_text", text };
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            id = "resp_mock_" + Calls, model = "mock-model-2026", status = Mode == "incomplete" ? "incomplete" : "completed",
            output = new[] { new { type = "message", content = new[] { part } } },
            usage = new { input_tokens = 123, input_tokens_details = new { cached_tokens = 23 }, output_tokens = 45 }
        }), Encoding.UTF8, "application/json") };
    }
}
