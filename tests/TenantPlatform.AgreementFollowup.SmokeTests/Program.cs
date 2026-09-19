using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web;
using TenantPlatform.Web.Email;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

var connection = Environment.GetEnvironmentVariable("AGREEMENT_TEST_CONNECTION") ?? throw new Exception("Set AGREEMENT_TEST_CONNECTION.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_agreement_tests") throw new Exception("Only the disposable tenant_agreement_tests database is allowed.");
var schema = "followup_test_" + Guid.NewGuid().ToString("N");
await using (var setup = new NpgsqlConnection(connection))
{
    await setup.OpenAsync();
    await using var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup); await cmd.ExecuteNonQueryAsync();
}
cs.SearchPath = schema;
Console.WriteLine($"Isolated schema: {schema}");
var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString).Options);
await using (var db = factory.CreateDbContext())
{
    Assert(!db.Database.HasPendingModelChanges(), "model matches migration snapshot");
    await db.Database.MigrateAsync();
    Assert(!(await db.Database.GetPendingMigrationsAsync()).Any(), "full migration chain applies");
}
var clock = new TestClock(new(2032, 2, 29, 6, 59, 0, TimeSpan.Zero));
var today = new DateOnly(2032, 2, 29);
var account = Guid.NewGuid(); var otherAccount = Guid.NewGuid();
var adminId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var readerId = Guid.NewGuid(); var editorId = Guid.NewGuid();
var strangerId = Guid.NewGuid(); var foreignId = Guid.NewGuid(); var org = Guid.NewGuid(); var foreignOrg = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Accounts.AddRange(new Account { Id = account, Name = "A" }, new Account { Id = otherAccount, Name = "B" });
    foreach (var id in new[] { adminId, ownerId, readerId, editorId, strangerId, foreignId })
    {
        db.Users.Add(new User { Id = id, FirstName = id == ownerId ? "Owner" : "Member", Email = $"{id}@example.test", PreferredLanguage = "en-GB" });
        var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = id == foreignId ? otherAccount : account, UserId = id };
        db.UserAccounts.Add(membership);
        if (id == adminId || id == foreignId)
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
    }
    db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Acme" }, new Organization { Id = foreignOrg, AccountId = otherAccount, Name = "Other" });
    await db.SaveChangesAsync();
}
var root = Path.Combine(Path.GetTempPath(), "followup-capture-" + Guid.NewGuid().ToString("N"));
var storage = new LocalAgreementDocumentStorage(Options.Create(new AgreementDocumentStorageOptions { RootPath = root }));
var options = Options.Create(new AgreementReminderOptions { ApplicationBaseUrl = "https://tenant.example.test", CapturePath = root });
AgreementService Service(Guid userId, Guid accountId) {
    var context = new UserContext(userId, accountId);
    return new(factory, context, new TenantAuthorizationService(factory, context), storage, NullLogger<AgreementService>.Instance, clock, options);
}
var admin = Service(adminId, account); var owner = Service(ownerId, account); var reader = Service(readerId, account);
var editor = Service(editorId, account); var stranger = Service(strangerId, account); var foreign = Service(foreignId, otherAccount);
SaveAgreementRequest Request(string title, DateOnly? notice = null) => new() { Title = title, CounterpartyOrganizationId = org, OwnerUserId = ownerId,
    Status = AgreementStatus.Active, StartDate = today.AddYears(-1), NoticeDeadline = notice };
var request = Request("Initial", today.AddDays(90)); request.Status = AgreementStatus.Draft;
request.EndDate = today.AddDays(120); request.RenewalDate = today.AddDays(150);
var agreement = await CreateLegacyAsync(account, request);
var initial = await owner.GetFollowupAsync(account, agreement);
Assert(initial.Deadlines.Count == 3 && initial.Deadlines.All(x => x.Deadline.Status == AgreementFollowupStatus.Untreated), "three independent occurrences created");
var notice = initial.Deadlines.Single(x => x.Deadline.Kind == AgreementDeadlineKind.Notice).Deadline.Id;
await owner.SetAccessAsync(account, agreement, readerId, AgreementAccessLevel.Read, initial.Revision);
await owner.SetAccessAsync(account, agreement, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account, agreement)).Revision);
Assert((await reader.GetFollowupAsync(account, agreement)).Deadlines.Count == 3, "reader sees deadlines and history");
await Expect<UnauthorizedAccessException>(() => reader.FollowupAsync(account, notice, Guid.Empty, AgreementFollowupAction.Start, null, null), "reader cannot follow up");
await Expect<UnauthorizedAccessException>(() => stranger.GetFollowupAsync(account, agreement), "ungranted account member cannot read history");
await Expect<UnauthorizedAccessException>(() => foreign.GetFollowupAsync(otherAccount, agreement), "foreign agreement/history lookup denied");
await Expect<UnauthorizedAccessException>(() => foreign.FollowupAsync(otherAccount, notice, Guid.Empty, AgreementFollowupAction.Start, null, null), "foreign deadline update denied");
Assert((await foreign.ListDeadlinesAsync(otherAccount, new())).Total == 0, "foreign list isolated");
await Expect<UnauthorizedAccessException>(() => admin.ListDeadlinesAsync(otherAccount, new()), "forged account list denied");
await Expect<UnauthorizedAccessException>(() => editor.GetReminderSettingsAsync(account), "only account admin manages defaults");
await Expect<AgreementValidationException>(() => Assign(strangerId), "assignment does not grant edit access");
await Expect<AgreementValidationException>(() => Assign(foreignId), "foreign assignee rejected");
await Assign(editorId);
Assert((await owner.GetFollowupAsync(account, agreement)).Deadlines.Single(x => x.Deadline.Id == notice).Deadline.ResponsibleUserId == editorId, "valid editor can be assigned");
await Assign(null);
await Act(AgreementFollowupAction.Start);
await Expect<AgreementValidationException>(() => Act(AgreementFollowupAction.Complete), "completion requires comment");
await Act(AgreementFollowupAction.Complete, "Renewal reviewed");
var completed = await owner.GetFollowupAsync(account, agreement);
Assert(completed.Deadlines.Single(x => x.Deadline.Id == notice).Deadline.Status == AgreementFollowupStatus.Completed &&
    completed.Deadlines.Count(x => x.Deadline.Status == AgreementFollowupStatus.Untreated) == 2, "one completion does not complete other deadlines");
await Act(AgreementFollowupAction.Reopen);
var stale = await owner.GetFollowupAsync(account, agreement);
await Act(AgreementFollowupAction.Comment, "Follow-up note");
await Expect<AgreementValidationException>(() => owner.FollowupAsync(account, notice, stale.Revision, AgreementFollowupAction.Comment, null, "Stale"), "stale follow-up rejected");
var editRequest = await owner.GetAsync(account, agreement); editRequest.NoticeDeadline = today.AddDays(95);
await owner.UpdateAsync(account, agreement, editRequest);
var replaced = await owner.GetFollowupAsync(account, agreement);
Assert(replaced.Deadlines.Single(x => x.Deadline.Id == notice).Deadline.State == AgreementDeadlineState.Replaced, "date change replaces occurrence");
Assert(replaced.Deadlines.Single(x => x.Deadline.Id == notice).History.Any(x => x.Comment == "Renewal reviewed"), "old comments and completion history preserved");
var newNotice = replaced.Deadlines.Single(x => x.Deadline.Kind == AgreementDeadlineKind.Notice && x.Deadline.State == AgreementDeadlineState.Current);
Assert(newNotice.Deadline.Status == AgreementFollowupStatus.Untreated, "replacement starts untreated");
editRequest = await owner.GetAsync(account, agreement); editRequest.NoticeDeadline = today.AddDays(90);
await owner.UpdateAsync(account, agreement, editRequest);
Assert((await owner.GetFollowupAsync(account, agreement)).Deadlines.Count(x => x.Deadline.Kind == AgreementDeadlineKind.Notice) == 3, "reused date gets fresh occurrence");
editRequest = await owner.GetAsync(account, agreement); editRequest.StartDate = editRequest.StartDate.AddDays(1);
await owner.UpdateAsync(account, agreement, editRequest);
Assert((await owner.GetFollowupAsync(account, agreement)).Deadlines.Count(x => x.Deadline.State == AgreementDeadlineState.Current) == 3, "start date correction keeps three current occurrences");
editRequest = await owner.GetAsync(account, agreement); editRequest.NoticeDeadline = null;
await owner.UpdateAsync(account, agreement, editRequest);
Assert(!(await owner.GetFollowupAsync(account, agreement)).Deadlines.Any(x => x.Deadline.Kind == AgreementDeadlineKind.Notice && x.Deadline.State == AgreementDeadlineState.Current), "removed date withdraws current occurrence");

var transport = new RecordingTransport();
AgreementReminderProcessor Processor() => new(factory, clock, transport, NullLogger<AgreementReminderProcessor>.Instance);
var remindersAgreement = await CreateLegacyAsync(account, Request("Reminder dates", today.AddDays(90)));
await Processor().RunAccountAsync(account);
Assert(transport.Messages.Count == 0, "reminders disabled by default");
var settings = await admin.GetReminderSettingsAsync(account); settings.Enabled = true;
await admin.SaveReminderSettingsAsync(account, settings);
await Processor().RunAccountAsync(account);
Assert(Count(remindersAgreement) == 0, "no send before account-local 08:00");
clock.Now = new(2032, 2, 29, 7, 0, 0, TimeSpan.Zero);
await Task.WhenAll(Processor().RunAccountAsync(account), Processor().RunAccountAsync(account));
Assert(Count(remindersAgreement) == 1, "parallel workers reserve and send exactly one 90-day reminder");
await Processor().RunAccountAsync(account);
Assert(Count(remindersAgreement) == 1, "repeated job is idempotent");
var reminderDetails = await owner.GetFollowupAsync(account, remindersAgreement);
var reminderDeadline = reminderDetails.Deadlines.Single().Deadline.Id;
Assert(reminderDetails.Deadlines.Single().Reminders.Count == 3, "unique 90/30/7 queue identities");
clock.Now = new DateTimeOffset(today.AddDays(60).ToDateTime(new(6, 0)), TimeSpan.Zero);
await Processor().RunAccountAsync(account);
Assert(Count(remindersAgreement) == 2, "30-day reminder across month and DST boundary");
clock.Now = new DateTimeOffset(today.AddDays(83).ToDateTime(new(6, 0)), TimeSpan.Zero);
await Processor().RunAccountAsync(account);
Assert(Count(remindersAgreement) == 3, "7-day reminder on exact date");
var latest = await owner.GetFollowupAsync(account, remindersAgreement);
await owner.FollowupAsync(account, reminderDeadline, latest.Revision, AgreementFollowupAction.Complete, null, "Done");
latest = await owner.GetFollowupAsync(account, remindersAgreement);
await owner.FollowupAsync(account, reminderDeadline, latest.Revision, AgreementFollowupAction.Reopen, null, null);
await Processor().RunAccountAsync(account);
Assert(Count(remindersAgreement) == 3, "reopening does not resend sent thresholds");

var currentDay = AgreementReminderSchedule.Today(clock.Now, "Europe/Oslo");
var catchup = await CreateLegacyAsync(account, Request("Catch-up", currentDay.AddDays(5)));
await Task.WhenAll(Processor().RunAccountAsync(account), Processor().RunAccountAsync(account));
var catchupLog = (await owner.GetFollowupAsync(account, catchup)).Deadlines.Single().Reminders;
Assert(Count(catchup) == 1 && catchupLog.Single(x => x.Status == AgreementReminderStatus.Sent).DaysBefore == 7 &&
    catchupLog.Count(x => x.Status == AgreementReminderStatus.Skipped) == 2, "late registration sends newest passed threshold only");
var past = await CreateLegacyAsync(account, Request("Overdue", currentDay.AddDays(-1)));
await Processor().RunAccountAsync(account);
Assert(Count(past) == 0 && (await owner.GetFollowupAsync(account, past)).Deadlines.Single().Reminders.All(x => x.Status == AgreementReminderStatus.Skipped), "no old reminders after deadline");
var overview = await owner.ListDeadlinesAsync(account, new());
Assert(overview.Items.Any(x => x.AgreementId == past) && overview.Overdue > 0, "default overview retains overdue deadlines");
Assert((await owner.ListDeadlinesAsync(account, new() { Search = "Acme", Kind = AgreementDeadlineKind.Notice })).Total > 0, "counterparty and deadline filters translate");
var zero = await CreateLegacyAsync(account, Request("Deadline day", currentDay));
await owner.SetReminderPreferencesAsync(account, zero, (await owner.GetFollowupAsync(account, zero)).Revision, AgreementReminderMode.Custom, [0]);
await Processor().RunAccountAsync(account);
Assert(Count(zero) == 1, "zero-day reminder on deadline day");
await Expect<AgreementValidationException>(() => owner.SetReminderPreferencesAsync(account, zero, (Guid.Empty), AgreementReminderMode.Custom, [-1]), "invalid preference request rejected");
ExpectSync(() => AgreementReminderSchedule.ValidateDays([7, 7]), "duplicate thresholds rejected");
ExpectSync(() => AgreementReminderSchedule.ValidateDays([-1]), "negative threshold rejected");
ExpectSync(() => AgreementReminderSchedule.ParseDays("7,1.5"), "fractional threshold rejected");
Assert(AgreementReminderSchedule.Scheduled(new(2032, 5, 29), 90, "Europe/Oslo", new(8, 0)) == new DateTimeOffset(2032, 2, 29, 7, 0, 0, TimeSpan.Zero), "leap-year 90-day calculation");
Assert(AgreementReminderSchedule.Scheduled(new(2026, 3, 29), 0, "Europe/Oslo", new(2, 30)) == new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), "missing DST time postponed");
Assert(AgreementReminderSchedule.Scheduled(new(2026, 10, 25), 0, "Europe/Oslo", new(2, 30)) == new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), "ambiguous DST time uses later instant");

var removed = await New("Removed date");
await Processor().PlanAccountAsync(account);
var r = await owner.GetAsync(account, removed); r.NoticeDeadline = null; await owner.UpdateAsync(account, removed, r);
Assert((await owner.GetFollowupAsync(account, removed)).Deadlines.Single().Reminders.All(x => x.Status == AgreementReminderStatus.Skipped), "date removal cancels pending reminders atomically");
await Processor().RunAccountAsync(account); Assert(Count(removed) == 0, "removed date never sent");
var archived = await New("Archive");
await Processor().PlanAccountAsync(account);
await owner.ArchiveAsync(account, archived, (await owner.GetFollowupAsync(account, archived)).Revision);
await Processor().RunAccountAsync(account);
Assert(Count(archived) == 0 && (await owner.GetAsync(account, archived)).IsArchived, "archive retains agreement and stops sends");
await Expect<AgreementValidationException>(() => owner.UpdateAsync(account, archived, r), "archive rejects edits");
var done = await New("Completed");
await Processor().PlanAccountAsync(account);
var doneDetails = await owner.GetFollowupAsync(account, done);
await owner.FollowupAsync(account, doneDetails.Deadlines.Single().Deadline.Id, doneDetails.Revision, AgreementFollowupAction.Complete, null, "Resolved");
await Processor().RunAccountAsync(account); Assert(Count(done) == 0, "completed follow-up not sent");
var revoked = await New("Revoked editor");
await owner.SetAccessAsync(account, revoked, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account, revoked)).Revision);
var revokedDetails = await owner.GetFollowupAsync(account, revoked);
await owner.FollowupAsync(account, revokedDetails.Deadlines.Single().Deadline.Id, revokedDetails.Revision, AgreementFollowupAction.Assign, editorId, null);
await Processor().PlanAccountAsync(account);
await owner.SetAccessAsync(account, revoked, editorId, null, (await owner.GetAsync(account, revoked)).Revision);
await Processor().RunAccountAsync(account);
Assert(Count(revoked) == 0 && !(await owner.GetFollowupAsync(account, revoked)).Deadlines.Single().Deadline.ResponsibleValid, "access loss before dispatch blocks sending and flags owner");
var departed = await New("Departed member");
await owner.SetAccessAsync(account, departed, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account, departed)).Revision);
var depDetails = await owner.GetFollowupAsync(account, departed);
await owner.FollowupAsync(account, depDetails.Deadlines.Single().Deadline.Id, depDetails.Revision, AgreementFollowupAction.Assign, editorId, null);
await Processor().PlanAccountAsync(account);
await using (var db = factory.CreateDbContext())
{
    db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == editorId)); await db.SaveChangesAsync();
}
await Processor().RunAccountAsync(account);
Assert(Count(departed) == 0 && !(await owner.GetFollowupAsync(account, departed)).Deadlines.Single().Deadline.ResponsibleValid, "membership loss prevents reminders despite stale grant");
await Expect<UnauthorizedAccessException>(() => editor.GetFollowupAsync(account, departed), "former member cannot read follow-up");

var retry = await New("Retry");
transport.Failures[retry] = 1;
await Processor().RunAccountAsync(account);
var retryLog = (await owner.GetFollowupAsync(account, retry)).Deadlines.Single().Reminders.Single(x => x.DaysBefore == 7);
Assert(retryLog.Status == AgreementReminderStatus.Failed && retryLog.Attempts == 1, "transport error persisted without secret details");
await Processor().RunAccountAsync(account); Assert(Count(retry) == 0, "retry waits for backoff");
clock.Now = clock.Now.AddMinutes(2);
await Processor().RunAccountAsync(account); Assert(Count(retry) == 1, "bounded retry succeeds after delay");
var alwaysFails = await New("Bounded failure"); transport.Failures[alwaysFails] = 100;
await Processor().RunAccountAsync(account);
clock.Now = clock.Now.AddMinutes(2); await Processor().RunAccountAsync(account);
clock.Now = clock.Now.AddMinutes(4); await Processor().RunAccountAsync(account);
clock.Now = clock.Now.AddHours(1); await Processor().RunAccountAsync(account);
Assert((await owner.GetFollowupAsync(account, alwaysFails)).Deadlines.Single().Reminders.Single(x => x.DaysBefore == 7).Attempts == 3, "automatic retries stop at three");

var unknown = await New("Unknown delivery");
await Processor().PlanAccountAsync(account);
await using (var db = factory.CreateDbContext())
{
    var d = await db.AgreementDeadlines.SingleAsync(x => x.AccountId == account && x.AgreementId == unknown);
    var q = await db.AgreementReminders.SingleAsync(x => x.AccountId == account && x.DeadlineId == d.Id && x.DaysBefore == 7);
    q.Status = AgreementReminderStatus.Processing; q.ReservationId = Guid.NewGuid(); q.ReservedUntilUtc = clock.Now.AddMinutes(-1); q.Attempts = 1;
    await db.SaveChangesAsync();
}
await Processor().RunAccountAsync(account);
Assert(Count(unknown) == 0 && (await owner.GetFollowupAsync(account, unknown)).Deadlines.Single().Reminders.Single(x => x.DaysBefore == 7).ResultKey == "FollowupDeliveryUnknown", "expired reservation is not blindly resent");
Assert((await owner.GetFollowupAsync(account, unknown)).Deadlines.Single().Reminders.Single(x => x.DaysBefore == 7).Attempts == 1, "unknown outcome preserves actual attempt count");

var legacy = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    db.Agreements.Add(new Agreement { Id = legacy, AccountId = account, Title = "Legacy", CounterpartyOrganizationId = org, OwnerUserId = ownerId,
        Status = AgreementStatus.Draft, Type = AgreementType.Other, StartDate = today, CurrentPeriodStartDate = today, NoticeDeadline = today.AddDays(1), CreatedByUserId = adminId, UpdatedByUserId = adminId,
        CreatedUtc = clock.Now, UpdatedUtc = clock.Now, Revision = Guid.NewGuid() });
    await db.SaveChangesAsync();
}
await Task.WhenAll(new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(account), new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(account));
await new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(account);
Assert((await owner.GetFollowupAsync(account, legacy)).Deadlines.Count == 1, "concurrent idempotent backfill creates one occurrence");
var bRequest = Request("Private B", currentDay.AddDays(5)); bRequest.OwnerUserId = foreignId; bRequest.CounterpartyOrganizationId = foreignOrg;
var bAgreement = await CreateLegacyAsync(otherAccount, bRequest);
var bSettings = await foreign.GetReminderSettingsAsync(otherAccount); bSettings.Enabled = true; await foreign.SaveReminderSettingsAsync(otherAccount, bSettings);
await Processor().RunAccountAsync(account); Assert(Count(bAgreement) == 0, "account worker cannot queue/send another account");
await Processor().RunAccountAsync(otherAccount);
Assert(transport.Messages.Single(x => x.AgreementId == bAgreement).AccountId == otherAccount &&
    transport.Messages.Single(x => x.AgreementId == bAgreement).Recipient == $"{foreignId}@example.test", "explicit account worker resolves only own recipient");

// Real adapter in Capture mode; SMTP spy throws if touched.
var smtpSpy = new SmtpSpy();
var localization = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance);
var localizer = new StringLocalizer<TenantPlatformResources>(localization);
var capture = new AgreementReminderTransport(options, new TestEnvironment(root), smtpSpy, localizer);
var captureMessage = new AgreementReminderMessage(Guid.NewGuid(), account, agreement, "test@example.test", "en-GB", "Test contract",
    AgreementDeadlineKind.Notice, currentDay);
await capture.SendAsync(captureMessage, default); await capture.SendAsync(captureMessage, default);
Assert(smtpSpy.Calls == 0 && Directory.GetFiles(root, "*.json").Length == 1, "local capture idempotent and never calls SMTP");
var json = await File.ReadAllTextAsync(Path.Combine(root, captureMessage.QueueId.ToString("N") + ".json"));
Assert(json.Contains("Agreement follow-up") && json.Contains("https://tenant.example.test/agreements/"), "localized captured message uses configured URL");
options.Value.TransportMode = "Smtp";
await Expect<AgreementValidationException>(() => capture.SendAsync(captureMessage, default), "development environment refuses real SMTP");
Assert(smtpSpy.Calls == 0, "no real emails in test");
Console.WriteLine("All agreement follow-up smoke tests passed.");

// These legacy fixtures intentionally retain the original one-deadline layout. New forms
// and their creation/validation are covered by the notice smoke suite.
async Task<Guid> CreateLegacyAsync(Guid accountId, SaveAgreementRequest r)
{
    await using var db = factory.CreateDbContext();
    var entity = new Agreement { Id = Guid.NewGuid(), AccountId = accountId, Title = r.Title,
        CounterpartyOrganizationId = r.CounterpartyOrganizationId, OwnerUserId = r.OwnerUserId,
        Type = r.Type, Status = r.Status, StartDate = r.StartDate, CurrentPeriodStartDate = r.StartDate,
        EndDate = r.EndDate, NoticeDeadline = r.NoticeDeadline, RenewalDate = r.RenewalDate,
        NoticeMode = r.NoticeDeadline.HasValue ? AgreementNoticeMode.Manual : AgreementNoticeMode.None,
        CreatedByUserId = r.OwnerUserId, UpdatedByUserId = r.OwnerUserId, CreatedUtc = clock.Now,
        UpdatedUtc = clock.Now, Revision = Guid.NewGuid() };
    db.Agreements.Add(entity); await db.SaveChangesAsync();
    await new AgreementDeadlineInitializer(factory, clock).EnsureAccountAsync(accountId);
    return entity.Id;
}
int Count(Guid id) => transport.Messages.Count(x => x.AgreementId == id);
Task<Guid> New(string title) => CreateLegacyAsync(account, Request(title, AgreementReminderSchedule.Today(clock.Now, "Europe/Oslo").AddDays(5)));
async Task Act(AgreementFollowupAction action, string? comment = null) =>
    await owner.FollowupAsync(account, notice, (await owner.GetFollowupAsync(account, agreement)).Revision, action, null, comment);
async Task Assign(Guid? id) =>
    await editor.FollowupAsync(account, notice, (await editor.GetFollowupAsync(account, agreement)).Revision, AgreementFollowupAction.Assign, id, null);
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
static async Task Expect<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name);
}
static void ExpectSync(Action action, string name)
{
    try { action(); } catch (AgreementValidationException) { Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name);
}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext> { public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class UserContext(Guid user, Guid account) : ICurrentUserContextService {
    public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account };
}
sealed class TestClock(DateTimeOffset initial) : TimeProvider { public DateTimeOffset Now { get; set; } = initial; public override DateTimeOffset GetUtcNow() => Now; }
sealed class RecordingTransport : IAgreementReminderTransport {
    public ConcurrentQueue<AgreementReminderMessage> Messages { get; } = new();
    public ConcurrentDictionary<Guid, int> Failures { get; } = new();
    public async Task<string?> SendAsync(AgreementReminderMessage message, CancellationToken ct) {
        await Task.Delay(30, ct);
        if (Failures.TryGetValue(message.AgreementId, out var remaining) && remaining > 0) {
            Failures[message.AgreementId] = remaining - 1; throw new IOException("Do not expose transport internals");
        }
        Messages.Enqueue(message); return "test:" + message.QueueId;
    }
}
sealed class SmtpSpy : IEmailSender {
    public int Calls { get; private set; }
    public Task SendAsync(string toAddress, string? replyToAddress, string subject, string body, string? inReplyToMessageId = null, string? references = null, CancellationToken cancellationToken = default) {
        Calls++; throw new Exception("Real SMTP must never run in tests.");
    }
}
sealed class TestEnvironment(string root) : IHostEnvironment {
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "TenantPlatform.Web";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
