using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
var schema = "notice_test_" + Guid.NewGuid().ToString("N");
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
    await db.GetService<IMigrator>().MigrateAsync("20260916154805_AddAgreementFollowupAndReminders");
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
// Migration regression: create old-schema data before upgrading, including completed follow-up and pending mail.
var migrated = new List<Guid>(); var legacyRevision = Guid.NewGuid(); var oldDeadline = Guid.NewGuid(); var oldReminder = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    foreach (var index in Enumerable.Range(0, 4))
    {
        var id = Guid.NewGuid(); migrated.Add(id);
        DateOnly? end = index == 0 ? today.AddYears(1) : null;
        DateOnly? renewal = index == 1 ? today.AddMonths(9) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agreements ("Id","AccountId","Title","Type","CounterpartyOrganizationId","OwnerUserId","Status","StartDate","EndDate","NoticeDeadline","RenewalDate","AutoRenew","CreatedUtc","UpdatedUtc","CreatedByUserId","UpdatedByUserId","Revision","DeadlinesInitialized")
            VALUES ({id},{account},{"Migration " + index},5,{org},{ownerId},2,{today},{end},{today.AddDays(30)},{renewal},{index == 3},{clock.Now},{clock.Now},{adminId},{adminId},{legacyRevision},true)
            """);
    }
    db.AgreementDeadlines.Add(new AgreementDeadline { Id = oldDeadline, AccountId = account, AgreementId = migrated[1],
        Kind = AgreementDeadlineKind.Notice, DueDate = today.AddDays(30), PeriodStartDate = today,
        Status = AgreementFollowupStatus.Completed, CreatedUtc = clock.Now, UpdatedUtc = clock.Now, CompletedUtc = clock.Now,
        CompletedByUserId = ownerId });
    db.AgreementDeadlineHistory.Add(new AgreementDeadlineHistory { Id = Guid.NewGuid(), AccountId = account, DeadlineId = oldDeadline,
        Kind = AgreementHistoryKind.Completed, Comment = "Keep this history", ActorUserId = ownerId, CreatedUtc = clock.Now });
    db.AgreementReminders.Add(new AgreementReminder { Id = oldReminder, AccountId = account, DeadlineId = oldDeadline,
        DaysBefore = 7, ScheduledUtc = clock.Now.AddDays(23) });
    await db.SaveChangesAsync();
    await db.Database.MigrateAsync();
    Assert(!db.Database.HasPendingModelChanges(), "migration snapshot matches model");
}
foreach (var (id, expected) in migrated.Zip(new[] { AgreementForm.FixedTerm, AgreementForm.Renewing, AgreementForm.Legacy, AgreementForm.Legacy }))
{
    var item = await admin.GetAsync(account, id);
    Assert(item.Form == expected && item.NoticeMode == AgreementNoticeMode.Manual && item.NoticeDeadline == today.AddDays(30) &&
        item.NoticeCount == null && item.Revision == legacyRevision, "migration preserves manual dates and classifies only unambiguous forms");
}
var preserved = await owner.GetFollowupAsync(account, migrated[1]);
Assert(preserved.Deadlines.Single().Deadline.Id == oldDeadline && preserved.Deadlines.Single().Deadline.Status == AgreementFollowupStatus.Completed &&
    preserved.Deadlines.Single().History.Single().Comment == "Keep this history", "migration preserves occurrence identity, completion and comments");
await using (var db = factory.CreateDbContext()) Assert((await db.AgreementReminders.SingleAsync(x => x.AccountId == account && x.Id == oldReminder)).Status == AgreementReminderStatus.Pending,
    "migration neither queues nor sends or modifies reminders");

Assert(AgreementNoticeRules.Calculate(new(2027, 1, 1), 30, AgreementNoticeUnit.Days, true) == new DateOnly(2026, 12, 2), "calendar days before renewal");
Assert(AgreementNoticeRules.Calculate(new(2027, 1, 1), 3, AgreementNoticeUnit.Months, true) == new DateOnly(2026, 10, 1), "calendar months before renewal");
Assert(AgreementNoticeRules.Calculate(new(2027, 3, 31), 1, AgreementNoticeUnit.Months, true) == new DateOnly(2027, 2, 28), "month end clamps to valid day");
Assert(AgreementNoticeRules.Calculate(new(2028, 1, 31), 1, AgreementNoticeUnit.Months, false) == new DateOnly(2028, 2, 29), "leap year addition");
foreach (var count in new[] { -1, 0, int.MaxValue }) ExpectSync(() => AgreementNoticeRules.Calculate(today, count, AgreementNoticeUnit.Months, true), "invalid interval rejected");
ExpectSync(() => AgreementNoticeRules.Calculate(DateOnly.MinValue, 1, AgreementNoticeUnit.Days, true), "lower date overflow rejected");
ExpectSync(() => AgreementNoticeRules.Calculate(DateOnly.MaxValue, 1, AgreementNoticeUnit.Months, false), "upper date overflow rejected");
ExpectSync(() => AgreementNoticeRules.Calculate(today, 1, (AgreementNoticeUnit)999, true), "unknown unit rejected");
var r = Request(AgreementForm.Renewing); r.RenewalDate = today.AddMonths(6); r.NoticeMode = AgreementNoticeMode.BeforeRenewal; r.NoticeCount = 3; r.NoticeUnit = AgreementNoticeUnit.Months;
r.NoticeDeadline = today.AddYears(5); // Client-supplied derived value must never win.
var renewalId = await admin.CreateAsync(account, r);
var detail = await owner.GetAsync(account, renewalId);
Assert(detail.NoticeDeadline == today.AddMonths(3), "server derives date and ignores submitted computed date");
var follow = await owner.GetFollowupAsync(account, renewalId);
var initialNotice = follow.Deadlines.Single(x => x.Deadline.Kind == AgreementDeadlineKind.Notice).Deadline.Id;
await owner.FollowupAsync(account, initialNotice, follow.Revision, AgreementFollowupAction.Complete, null, "Reviewed period");
detail = await owner.GetAsync(account, renewalId); detail.StartDate = detail.StartDate.AddDays(1);
await owner.UpdateAsync(account, renewalId, detail);
follow = await owner.GetFollowupAsync(account, renewalId);
Assert(follow.Deadlines.Single(x => x.Deadline.Id == initialNotice).Deadline.Status == AgreementFollowupStatus.Completed && follow.Deadlines.Count == 2,
    "start date correction does not create a new period or lose completion");
detail = await owner.GetAsync(account, renewalId); detail.RenewalDate = today.AddMonths(7);
await owner.UpdateAsync(account, renewalId, detail);
follow = await owner.GetFollowupAsync(account, renewalId);
Assert((await owner.GetAsync(account, renewalId)).NoticeDeadline == today.AddMonths(4) &&
    follow.Deadlines.Single(x => x.Deadline.Id == initialNotice).Deadline.State == AgreementDeadlineState.Replaced &&
    follow.Deadlines.Single(x => x.Deadline.Id == initialNotice).History.Any(x => x.Comment == "Reviewed period"), "renewal date recalculates and retains old completion history");
var currentNotice = follow.Deadlines.Single(x => x.Deadline.Kind == AgreementDeadlineKind.Notice && x.Deadline.State == AgreementDeadlineState.Current).Deadline.Id;
await owner.FollowupAsync(account, currentNotice, follow.Revision, AgreementFollowupAction.Complete, null, "Second review");
detail = await owner.GetAsync(account, renewalId); detail.BeginNewPeriod = true; detail.NewPeriodStartDate = today;
await owner.UpdateAsync(account, renewalId, detail);
follow = await owner.GetFollowupAsync(account, renewalId);
Assert(follow.Deadlines.Single(x => x.Deadline.Id == currentNotice).Deadline.State == AgreementDeadlineState.Replaced &&
    follow.Deadlines.Where(x => x.Deadline.State == AgreementDeadlineState.Current).All(x => x.Deadline.Status == AgreementFollowupStatus.Untreated),
    "explicit new period starts fresh follow-up even when due dates are unchanged");
detail = await owner.GetAsync(account, renewalId); detail.NoticeMode = AgreementNoticeMode.Manual; var manual = detail.NoticeDeadline;
detail.RenewalDate = today.AddMonths(9); await owner.UpdateAsync(account, renewalId, detail);
Assert((await owner.GetAsync(account, renewalId)).NoticeDeadline == manual, "switch to manual preserves effective date on renewal change");
await Invalid(x => { x.Form = AgreementForm.Renewing; x.NoticeMode = AgreementNoticeMode.BeforeRenewal; x.NoticeCount = 3; x.NoticeUnit = AgreementNoticeUnit.Months; }, "calculated notice needs renewal date");
await Invalid(x => { x.Form = AgreementForm.Ongoing; x.NoticeCount = 0; }, "ongoing needs positive duration");
await Invalid(x => x.Form = (AgreementForm)999, "unknown form rejected");
await Invalid(x => x.NoticeMode = (AgreementNoticeMode)999, "unknown mode rejected");
var early = Request(AgreementForm.Renewing); early.RenewalDate = early.StartDate.AddDays(1); early.NoticeMode = AgreementNoticeMode.BeforeRenewal; early.NoticeCount = 3; early.NoticeUnit = AgreementNoticeUnit.Months;
var earlyId = await admin.CreateAsync(account, early); Assert((await admin.GetAsync(account, earlyId)).NoticeDeadline < early.StartDate, "calculated notice before contract start allowed");

var ongoing = await admin.CreateAsync(account, Request(AgreementForm.Ongoing));
Assert((await owner.GetFollowupAsync(account, ongoing)).Deadlines.Count == 0, "ongoing agreement has no artificial deadline before notice");
await owner.SetAccessAsync(account, ongoing, readerId, AgreementAccessLevel.Read, (await owner.GetAsync(account, ongoing)).Revision);
await owner.SetAccessAsync(account, ongoing, editorId, AgreementAccessLevel.Edit, (await owner.GetAsync(account, ongoing)).Revision);
foreach (var action in new[] { AgreementNoticeAction.Registered, AgreementNoticeAction.Corrected, AgreementNoticeAction.Withdrawn })
{
    await Expect<UnauthorizedAccessException>(() => reader.RecordTerminationAsync(account, ongoing, new() { Action = action }), "reader cannot change termination");
    await Expect<UnauthorizedAccessException>(() => stranger.RecordTerminationAsync(account, ongoing, new() { Action = action }), "ungranted member cannot change termination");
    await Expect<UnauthorizedAccessException>(() => foreign.RecordTerminationAsync(otherAccount, ongoing, new() { Action = action }), "cross-account termination denied");
    await Expect<UnauthorizedAccessException>(() => admin.RecordTerminationAsync(otherAccount, ongoing, new() { Action = action }), "forged account denied");
}
await Act(ongoing, AgreementNoticeAction.Registered, new(2027, 1, 15));
detail = await reader.GetAsync(account, ongoing);
Assert(detail.NoticeSnapshot.CessationDate == new DateOnly(2027, 4, 15) && detail.Status == AgreementStatus.Active &&
    detail.TerminationRegisteredByName != null && detail.NoticeSnapshot.RegisteredUtc == clock.Now, "registration snapshots duration and actor without terminating status");
Assert((await owner.GetFollowupAsync(account, ongoing)).Deadlines.Single().Deadline.Kind == AgreementDeadlineKind.Cessation, "one cessation deadline, no duplicate expiry");
await Expect<AgreementValidationException>(() => Act(ongoing, AgreementNoticeAction.Registered, today), "second active notice rejected");
detail = await owner.GetAsync(account, ongoing); detail.NoticeCount = 6; await owner.UpdateAsync(account, ongoing, detail);
Assert((await owner.GetAsync(account, ongoing)).NoticeSnapshot.CessationDate == new DateOnly(2027, 4, 15), "general duration change does not alter snapshot cessation");
await Expect<AgreementValidationException>(() => Act(ongoing, AgreementNoticeAction.Corrected, today), "correction requires reason");
detail = await owner.GetAsync(account, ongoing); detail.Form = AgreementForm.FixedTerm; detail.EndDate = today.AddYears(1);
await Expect<AgreementValidationException>(() => owner.UpdateAsync(account, ongoing, detail), "cannot change form with active notice");
await Act(ongoing, AgreementNoticeAction.Corrected, new(2027, 2, 15), "Correct effective date");
detail = await owner.GetAsync(account, ongoing);
Assert(detail.NoticeSnapshot.CessationDate == new DateOnly(2027, 5, 15) && detail.NoticeSnapshot.RegisteredCount == 3 &&
    detail.NoticeHistory.Single(x => x.Action == AgreementNoticeAction.Corrected).Before.CessationDate == new DateOnly(2027, 4, 15), "correction uses original snapshot and preserves old values");
Assert((await owner.GetFollowupAsync(account, ongoing)).Deadlines.Count == 2, "correction replaces cessation occurrence");
// Bring cessation into the future relative to the deterministic test clock, then queue notices without sending.
await Act(ongoing, AgreementNoticeAction.Corrected, today, "Future notice");
var transport = new RecordingTransport(); var processor = new AgreementReminderProcessor(factory, clock, transport, NullLogger<AgreementReminderProcessor>.Instance);
var settings = await admin.GetReminderSettingsAsync(account); settings.Enabled = true; await admin.SaveReminderSettingsAsync(account, settings);
await processor.PlanAccountAsync(account);
var beforeWithdraw = await owner.GetFollowupAsync(account, ongoing);
var activeCessation = beforeWithdraw.Deadlines.Single(x => x.Deadline.State == AgreementDeadlineState.Current);
Assert(activeCessation.Reminders.Any(x => x.Status == AgreementReminderStatus.Pending), "cessation uses existing reminder planner");
await Act(ongoing, AgreementNoticeAction.Corrected, today.AddDays(1), "Correct queued date");
var correctedQueue = await owner.GetFollowupAsync(account, ongoing);
Assert(correctedQueue.Deadlines.Single(x => x.Deadline.Id == activeCessation.Deadline.Id).Reminders.All(x => x.Status == AgreementReminderStatus.Skipped),
    "correction cancels old pending queue atomically");
await processor.PlanAccountAsync(account); await processor.PlanAccountAsync(account);
activeCessation = (await owner.GetFollowupAsync(account, ongoing)).Deadlines.Single(x => x.Deadline.State == AgreementDeadlineState.Current);
Assert(activeCessation.Reminders.Count == 3 && activeCessation.Reminders.Any(x => x.Status == AgreementReminderStatus.Pending), "replacement reminders are planned without duplicates");
await Act(ongoing, AgreementNoticeAction.Withdrawn, today, "Notice withdrawn");
detail = await owner.GetAsync(account, ongoing); follow = await owner.GetFollowupAsync(account, ongoing);
Assert(detail.NoticeSnapshot.CessationDate == null && detail.NoticeHistory.Any(x => x.Action == AgreementNoticeAction.Withdrawn) &&
    follow.Deadlines.All(x => x.Deadline.State != AgreementDeadlineState.Current) &&
    follow.Deadlines.Single(x => x.Deadline.Id == activeCessation.Deadline.Id).Reminders.All(x => x.Status == AgreementReminderStatus.Skipped), "withdrawal retains history, removes active cessation and cancels queued reminders");
clock.Now = clock.Now.AddDays(89); await processor.RunAccountAsync(account);
Assert(!transport.Messages.Any(x => x.AgreementId == ongoing), "withdrawn cessation reminders never sent");
// Two registrations race using one revision; exactly one transaction can win.
var raceRequest = new AgreementTerminationRequest { Revision = detail.Revision, EffectiveDate = today };
var race = await Task.WhenAll(TryRegister(), TryRegister());
Assert(race.Count(x => x) == 1, "concurrent registration has exactly one winner");
follow = await owner.GetFollowupAsync(account, ongoing);
Assert(follow.Deadlines.Count(x => x.Deadline.State == AgreementDeadlineState.Current) == 1, "concurrent writes create one active deadline");
var beforeCount = follow.Deadlines.Count;
for (var i = 0; i < 3; i++) { detail = await owner.GetAsync(account, ongoing); await owner.UpdateAsync(account, ongoing, detail); }
Assert((await owner.GetFollowupAsync(account, ongoing)).Deadlines.Count == beforeCount, "repeated synchronization is idempotent");
await Act(ongoing, AgreementNoticeAction.Withdrawn, today);
detail = await owner.GetAsync(account, ongoing); detail.Form = AgreementForm.FixedTerm; detail.EndDate = today.AddYears(2); detail.NoticeMode = AgreementNoticeMode.None;
await owner.UpdateAsync(account, ongoing, detail);
detail = await owner.GetAsync(account, ongoing);
Assert(detail.NoticeCount == null && detail.RenewalDate == null && detail.NoticeHistory.Any(x => x.Before.Form == AgreementForm.Ongoing && x.After.Form == AgreementForm.FixedTerm), "form change normalizes fields and preserves previous rules");
Assert((await owner.GetFollowupAsync(account, ongoing)).Deadlines.Single(x => x.Deadline.State == AgreementDeadlineState.Current).Deadline.Kind == AgreementDeadlineKind.Expiry, "form change creates only relevant expiry");
await Expect<UnauthorizedAccessException>(() => foreign.GetAsync(otherAccount, ongoing), "history is tenant isolated");
// An actual renewal and a rule edit race with the same agreement revision.
var periodEdit = await owner.GetAsync(account, renewalId);
var ruleEdit = await owner.GetAsync(account, renewalId);
foreach (var x in new[] { periodEdit, ruleEdit }) { x.NoticeMode = AgreementNoticeMode.BeforeRenewal; x.NoticeCount = 2; x.NoticeUnit = AgreementNoticeUnit.Months; }
periodEdit.BeginNewPeriod = true; periodEdit.NewPeriodStartDate = today.AddYears(1); periodEdit.RenewalDate = today.AddYears(2);
ruleEdit.NoticeCount = 4;
var editRace = await Task.WhenAll(TryUpdate(periodEdit), TryUpdate(ruleEdit));
Assert(editRace.Count(x => x) == 1, "simultaneous renewal and rule edit serialize through revision");
var raceResult = await owner.GetAsync(account, renewalId);
Assert(raceResult.NoticeDeadline == AgreementNoticeRules.Calculate(raceResult.RenewalDate!.Value, raceResult.NoticeCount, raceResult.NoticeUnit, true),
    "concurrent rule edit leaves a consistent derived date");
await using (var db = factory.CreateDbContext())
{
    var currentDeadlines = await db.AgreementDeadlines.Where(x => x.AccountId == account && x.AgreementId == renewalId && x.State == AgreementDeadlineState.Current).ToListAsync();
    Assert(currentDeadlines.Count == 2 && currentDeadlines.All(x => x.PeriodStartDate == raceResult.CurrentPeriodStartDate), "concurrent edits preserve one coherent current period");
}
Console.WriteLine("All agreement notice smoke tests passed; transport is an in-memory capture only.");

SaveAgreementRequest Request(AgreementForm form) => new() { Title = "Notice test", Form = form, CounterpartyOrganizationId = org, OwnerUserId = ownerId,
    Status = AgreementStatus.Active, StartDate = today.AddYears(-1), NoticeCount = form == AgreementForm.Ongoing ? 3 : null,
    NoticeUnit = form == AgreementForm.Ongoing ? AgreementNoticeUnit.Months : null };
async Task Invalid(Action<SaveAgreementRequest> change, string name) { var x = Request(AgreementForm.FixedTerm); x.EndDate = today.AddYears(1); change(x); await Expect<AgreementValidationException>(() => admin.CreateAsync(account, x), name); }
async Task Act(Guid id, AgreementNoticeAction action, DateOnly effective, string? comment = null) => await editor.RecordTerminationAsync(account, id,
    new() { Action = action, EffectiveDate = effective, Comment = comment, Revision = (await editor.GetAsync(account, id)).Revision });
async Task<bool> TryUpdate(AgreementDetailsDto request) { try { await owner.UpdateAsync(account, renewalId, request); return true; } catch (AgreementValidationException ex) when (ex.Message == "AgreementConcurrencyConflict") { return false; } }
async Task<bool> TryRegister() { try { await owner.RecordTerminationAsync(account, ongoing, raceRequest); return true; } catch (AgreementValidationException ex) when (ex.Message == "AgreementConcurrencyConflict") { return false; } }
static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
static async Task Expect<T>(Func<Task> action, string name) where T : Exception { try { await action(); } catch (T) { Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name); }
static void ExpectSync(Action action, string name) { try { action(); } catch (AgreementValidationException) { Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name); }
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext> { public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class UserContext(Guid user, Guid account) : ICurrentUserContextService { public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account }; }
sealed class TestClock(DateTimeOffset initial) : TimeProvider { public DateTimeOffset Now { get; set; } = initial; public override DateTimeOffset GetUtcNow() => Now; }
sealed class RecordingTransport : IAgreementReminderTransport {
    public ConcurrentQueue<AgreementReminderMessage> Messages { get; } = new();
    public Task<string?> SendAsync(AgreementReminderMessage message, CancellationToken ct) { Messages.Enqueue(message); return Task.FromResult<string?>("test:" + message.QueueId); }
}
