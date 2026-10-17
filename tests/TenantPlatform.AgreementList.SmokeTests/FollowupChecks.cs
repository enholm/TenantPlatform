using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

static class FollowupChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL followup reader: " + name); Console.WriteLine($"PASS {provider} followup reader: {name}"); }
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var owner = Guid.NewGuid(); var reader = Guid.NewGuid(); var editor = Guid.NewGuid();
        var admin = Guid.NewGuid(); var stranger = Guid.NewGuid(); var foreign = Guid.NewGuid(); var platform = Guid.NewGuid();
        var member = Guid.NewGuid(); var party = Guid.NewGuid(); var foreignParty = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 16, 22, 30, 0, TimeSpan.Zero); var today = new DateOnly(2026, 10, 17); var clock = new Clock(now);
        Agreement Make(string title, bool foreignAccount = false) => new()
        {
            Id = Guid.NewGuid(), AccountId = foreignAccount ? b : a, OwnerUserId = foreignAccount ? foreign : owner,
            CounterpartyOrganizationId = foreignAccount ? foreignParty : party, Title = title, Type = AgreementType.Other, Status = AgreementStatus.Active,
            StartDate = new(2020, 1, 1), CurrentPeriodStartDate = new(2026, 1, 1), EndDate = today.AddDays(30), NoticeDeadline = today,
            CreatedUtc = now, UpdatedUtc = now, CreatedByUserId = owner, UpdatedByUserId = owner, Revision = Guid.NewGuid()
        };
        var target = Make("Followup target"); var lazy = Make("Lazy target"); var untouched = Make("Untouched"); untouched.OwnerUserId = stranger;
        var foreignAgreement = Make("Foreign", true);
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = a, Name = "Followup A" }, new Account { Id = b, Name = "Followup B" });
            foreach (var id in new[] { owner, reader, editor, admin, stranger, foreign, platform })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = id == foreign ? "foreign-secret" : "Local", LastName = id.ToString(), IsActive = true, IsPlatformAdmin = id == platform });
                var membership = new UserAccount { Id = id == reader ? member : Guid.NewGuid(), AccountId = id == foreign ? b : a, UserId = id };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Organizations.AddRange(new Organization { Id = party, AccountId = a, Name = "Local party" }, new Organization { Id = foreignParty, AccountId = b, Name = "foreign-secret party" });
            db.Agreements.AddRange(target, lazy, untouched, foreignAgreement);
            foreach (var agreement in new[] { target, lazy })
            foreach (var (id, level) in new[] { (reader, AgreementAccessLevel.Read), (editor, AgreementAccessLevel.Edit) })
                db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = a, AgreementId = agreement.Id, UserId = id, Level = level });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, owner, a)); var auth = new AuthorizationService(actor, new AuthorizationData(factory), clock);
        var service = new AgreementFollowupReader(auth, new AgreementFollowupData(factory), clock);
        async Task<AgreementFollowupDetails> Read(Guid? id = null)
        {
            guard.Protect = true;
            try { return await service.ReadAsync(a, id ?? target.Id); }
            finally { guard.Protect = false; }
        }
        async Task Denied(Func<Task> action, string name)
        {
            try { await action(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        var accountReader = new AccountContextReader(actor, new AuthorizationData(factory), new AccountContextData(factory));
        guard.Protect = true;
        try
        {
            Check((await accountReader.ReadAsync()).Single().Id == a && await accountReader.ReadSelectedNameAsync() == "Followup A", "Application account context exposes only the actor's own account");
            var deadlineReader = new AgreementDeadlineReader(auth, new AgreementDeadlineData(factory), clock);
            Check((await deadlineReader.ReadAsync(a, new())).Total == 0, "Application account deadline read does not initialize missing deadlines");
        }
        finally { guard.Protect = false; }
        var empty = await Read();
        Check(!empty.DeadlinesInitialized && empty.Deadlines.Count == 0 && empty.TimeZoneId == "Europe/Oslo" && empty.AccountDays.SequenceEqual(new[] { 90, 30, 7 }), "missing initialization/settings is a valid empty, write-free result with existing defaults");
        await using (var db = factory.CreateDbContext()) Check(!await db.AgreementDeadlines.AnyAsync(x => x.AccountId == a) && !await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == a), "direct empty reader never creates dates or settings");
        AgreementDeadline Deadline(Agreement agreement, AgreementDeadlineState state, AgreementFollowupStatus status, Guid? assigned, int offset) => new()
        {
            Id = Guid.NewGuid(), AccountId = agreement.AccountId, AgreementId = agreement.Id, Kind = AgreementDeadlineKind.Notice,
            DueDate = today.AddDays(offset), PeriodStartDate = agreement.CurrentPeriodStartDate, State = state, Status = status, AssignedUserId = assigned, CreatedUtc = now, UpdatedUtc = now
        };
        var current = Deadline(target, AgreementDeadlineState.Current, AgreementFollowupStatus.Untreated, foreign, 0);
        var replaced = Deadline(target, AgreementDeadlineState.Replaced, AgreementFollowupStatus.Completed, reader, -1);
        var withdrawn = Deadline(target, AgreementDeadlineState.Withdrawn, AgreementFollowupStatus.InReview, null, -2);
        var hidden = Deadline(untouched, AgreementDeadlineState.Current, AgreementFollowupStatus.Untreated, stranger, 0);
        var outside = Deadline(foreignAgreement, AgreementDeadlineState.Current, AgreementFollowupStatus.Untreated, foreign, 0);
        await using (var db = factory.CreateDbContext())
        {
            (await db.Agreements.SingleAsync(x => x.Id == target.Id)).DeadlinesInitialized = true;
            db.AgreementDeadlines.AddRange(current, replaced, withdrawn, hidden, outside);
            foreach (var deadline in new[] { current, replaced, withdrawn, hidden, outside })
                db.AgreementDeadlineHistory.Add(new() { Id = Guid.NewGuid(), AccountId = deadline.AccountId, DeadlineId = deadline.Id, ActorUserId = foreign, AssignedUserId = foreign, CreatedUtc = now, Kind = AgreementHistoryKind.Commented, Comment = deadline == hidden ? "hidden-history" : "visible history" });
            db.AgreementDeadlineHistory.Add(new() { Id = Guid.NewGuid(), AccountId = a, DeadlineId = current.Id, CreatedUtc = now.AddSeconds(1), Kind = AgreementHistoryKind.Created });
            foreach (var (recipient, days, result) in new (Guid?, int, string?)[] { (owner, 90, "FollowupTransportFailed"), (foreign, 30, "raw-secret-result"), (null, 7, null) })
                db.AgreementReminders.Add(new() { Id = Guid.NewGuid(), AccountId = a, DeadlineId = current.Id, DaysBefore = days, RecipientUserId = recipient,
                    RecipientAddress = "stale-secret@outside.test", ScheduledUtc = now.AddMinutes(90 - days), Status = AgreementReminderStatus.Failed,
                    Attempts = 1, LastAttemptUtc = now, TransportId = "transport-token-secret", ResultKey = result });
            db.AgreementReminders.Add(new() { Id = Guid.NewGuid(), AccountId = b, DeadlineId = outside.Id, DaysBefore = 7, ScheduledUtc = now, RecipientUserId = foreign, RecipientAddress = "foreign-secret@outside.test" });
            await db.SaveChangesAsync();
        }
        var detail = await Read();
        Check(detail.CanEdit && detail.CanArchive && detail.Deadlines.Select(x => x.Deadline.Id).SequenceEqual(new[] { current.Id, replaced.Id, withdrawn.Id }), "owner flags; all states/statuses preserved and sorted by state/date");
        Check(detail.Deadlines[0].Deadline.ResponsibleName == "" && !detail.Deadlines[0].Deadline.ResponsibleValid && !detail.Deadlines[1].Deadline.ResponsibleValid && detail.Deadlines[2].Deadline.ResponsibleValid, "account-bounded responsible names and existing edit eligibility validity");
        Check(detail.Deadlines[0].History.Count == 2 && detail.Deadlines[0].History[0].Actor == "" && detail.Deadlines[0].History[0].Assignee == "" && detail.Deadlines[0].History[1].Actor is null, "foreign/missing names retain history rows; system actor stays distinct");
        var reminders = detail.Deadlines[0].Reminders;
        Check(reminders.Count == 3 && reminders[0].Recipient == owner + "@example.test" && reminders[1].Recipient is null && reminders[1].ResultKey is null && reminders[0].ResultKey == "FollowupTransportFailed", "editable delivery summaries use current member identity and safe result keys, never raw stored addresses");
        var serialized = JsonSerializer.Serialize(detail);
        Check(!serialized.Contains("foreign-secret") && !serialized.Contains("stale-secret") && !serialized.Contains("transport-token") && !serialized.Contains("hidden-history") && !serialized.Contains("TransportId") && !serialized.Contains("ReservationId"), "no unrelated names, transport identifiers, tokens, raw queue addresses or unrelated history in result");
        Check(detail.Editors.Select(x => x.Id).ToHashSet().SetEquals(new[] { owner, editor, admin }), "existing assignment candidates exclude Read-only and foreign users");
        actor.Current = new(true, reader, a); var readOnly = await Read();
        Check(!readOnly.CanEdit && !readOnly.CanArchive && readOnly.Editors.Count == 0 && readOnly.Deadlines.Count == 3 && readOnly.Deadlines.All(x => x.Reminders.Count == 0), "Read can inspect authorized deadlines/history, without edit candidates or delivery queue summaries");
        actor.Current = new(true, editor, a); Check((await Read()).CanEdit && !(await Read()).CanArchive && (await Read()).Deadlines[0].Reminders.Count == 3, "Edit can follow up but cannot archive/manage sharing");
        actor.Current = new(true, admin, a); Check((await Read()).CanArchive && (await Read(untouched.Id)).CanEdit, "AccountAdmin reads account resources");
        await Denied(() => Read(foreignAgreement.Id), "foreign agreement denied even to account admin");
        actor.Current = new(true, stranger, a); await Denied(() => Read(), "unavailable agreement denied");
        await Denied(() => Read(Guid.NewGuid()), "unknown agreement denied");
        actor.Current = new(true, platform, a); await Denied(() => Read(), "PlatformAdmin has no account override");
        actor.Current = new(true, platform, b); await Denied(() => service.ReadAsync(b, foreignAgreement.Id), "PlatformAdmin without membership denied");
        actor.Current = new(false, owner, a); await Denied(() => Read(), "missing authentication");
        actor.Current = new(true, owner, null); await Denied(() => Read(), "missing account");
        actor.Current = new(true, owner, b); await Denied(() => Read(), "wrong account");
        actor.Current = new(true, Guid.NewGuid(), a); await Denied(() => Read(), "missing user/membership");
        actor.Current = new(true, owner, a);
        await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == owner)).IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => Read(), "inactive user");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == owner)).IsActive = true;
            var row = await db.Agreements.SingleAsync(x => x.Id == target.Id); row.IsArchived = true; row.Status = AgreementStatus.Terminated; await db.SaveChangesAsync();
        }
        var archived = await Read();
        Check(archived.IsArchived && !archived.CanEdit && !archived.CanArchive && archived.Editors.Count == 0 && archived.Deadlines.Count == 3, "archived/terminated remains readable without action controls");
        await using (var db = factory.CreateDbContext()) { (await db.Agreements.SingleAsync(x => x.Id == target.Id)).IsArchived = false; await db.SaveChangesAsync(); }
        Check((await Read()).Deadlines[0].Deadline.DaysLeft == 0, "default Oslo date uses account calendar day across UTC midnight");
        await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Add(new() { AccountId = a, TimeZoneId = "UTC", Enabled = true, Days = [30, 7] }); await db.SaveChangesAsync(); }
        Check((await Read()).Deadlines[0].Deadline.DaysLeft == 1 && (await Read()).AccountDays.SequenceEqual(new[] { 30, 7 }), "saved UTC and reminder settings override defaults without changing stored due dates");
        await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Remove(await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == a)); await db.SaveChangesAsync(); }
        foreach (var instant in new[] { new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), new(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 1, 30, 0, TimeSpan.Zero) })
        {
            var counting = new AttentionClock(instant); var dst = new AgreementFollowupReader(auth, new AgreementFollowupData(factory), counting);
            guard.Protect = true; AgreementFollowupDetails result;
            try { result = await dst.ReadAsync(a, target.Id); } finally { guard.Protect = false; }
            Check(counting.Reads == 1 && result.Deadlines[0].Deadline.DaysLeft == today.DayNumber - DateOnly.FromDateTime(instant.UtcDateTime).DayNumber, "single reference instant and DST day arithmetic: " + instant.ToString("O"));
        }
        actor.Current = new(true, reader, a);
        await using (var db = factory.CreateDbContext()) { db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == a && x.AgreementId == target.Id && x.UserId == reader)); await db.SaveChangesAsync(); }
        await Denied(() => Read(), "revoked grant takes effect on same reader");
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = a, AgreementId = target.Id, UserId = reader, Level = AgreementAccessLevel.Read });
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == member)); await db.SaveChangesAsync();
        }
        await Denied(() => Read(), "revoked membership takes effect on same reader");
        await using (var db = factory.CreateDbContext()) { db.UserAccounts.Add(new() { Id = member, AccountId = a, UserId = reader }); await db.SaveChangesAsync(); }
        AgreementService Web(Guid id)
        {
            var context = new WebContext(id, a); var shared = new AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new AuthorizationData(factory), clock);
            return new(null!, new ListProbe(), new AttentionProbe(), new DashboardProbe(), new InsightsProbe(), new AgreementDetailsReader(shared, new AgreementDetailsData(factory)), new AgreementFollowupReader(shared, new AgreementFollowupData(factory), clock), new AgreementDeadlineReader(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDeadlineData(factory), clock), new AgreementInitializationService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDeadlineInitializer(factory, clock)), new AgreementFollowupWriter(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementFollowupWriteData(factory), clock), new AgreementIndexService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementIndexData(factory), clock), new AgreementDocumentService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDocumentData(factory, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgreementDocumentData>.Instance), null!, clock), new AgreementManagementService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementManagementData(factory), clock), new AgreementFinancialService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementFinancialData(factory), clock), new AgreementAnalysisService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementAnalysisData(factory), null!, new AgreementManagementService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementManagementData(factory), clock), new AgreementAnalysisCleanupService(new AgreementAnalysisCleanupData(factory), null!, clock, new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.AgreementAnalysisCleanup)), clock));
        }
        await Denied(() => Web(stranger).GetFollowupAsync(a, lazy.Id), "denied GUI read cannot start initialization");
        await using (var db = factory.CreateDbContext()) Check(!(await db.Agreements.SingleAsync(x => x.Id == lazy.Id)).DeadlinesInitialized && !await db.AgreementDeadlines.AnyAsync(x => x.AgreementId == lazy.Id), "denial leaves initialization flag and rows untouched");
        var gui = Web(reader);
        if (provider == "PostgreSQL")
        {
            await Task.WhenAll(gui.GetFollowupAsync(a, lazy.Id), Web(editor).GetAsync(a, lazy.Id));
            Check(true, "concurrent detail/followup GUI initialization shares revision retry");
        }
        var initialized = await gui.GetFollowupAsync(a, lazy.Id);
        Check(initialized.DeadlinesInitialized && initialized.Deadlines.Count == 2 && !initialized.CanEdit, "GUI Read compatibility initializes only selected lazy agreement");
        Guid revision; int count;
        await using (var db = factory.CreateDbContext())
        {
            revision = (await db.Agreements.SingleAsync(x => x.Id == lazy.Id)).Revision;
            count = await db.AgreementDeadlines.CountAsync(x => x.AgreementId == lazy.Id);
            Check(!(await db.Agreements.SingleAsync(x => x.Id == untouched.Id)).DeadlinesInitialized && !(await db.Agreements.SingleAsync(x => x.Id == foreignAgreement.Id)).DeadlinesInitialized, "neither GUI facade backfills unrelated or foreign agreements");
        }
        await gui.GetAsync(a, lazy.Id); await gui.GetFollowupAsync(a, lazy.Id);
        await using (var db = factory.CreateDbContext()) Check((await db.Agreements.SingleAsync(x => x.Id == lazy.Id)).Revision == revision && await db.AgreementDeadlines.CountAsync(x => x.AgreementId == lazy.Id) == count, "repeated mixed GUI calls create no new revisions or duplicate deadlines");
        await RenderPanel(gui, new WebContext(reader, a), lazy.Id, initialized.Revision, Check);
        await RenderPanel(Web(owner), new WebContext(owner, a), target.Id, target.Revision, Check);
        actor.Current = new(true, reader, a);
        var accountDeadlines = new AgreementDeadlineReader(auth, new AgreementDeadlineData(factory), clock);
        guard.Protect = true;
        try
        {
            var page = await accountDeadlines.ReadAsync(a, new() { State = null, IncludeCompleted = true });
            Check(page.Items.All(x => x.AgreementId == target.Id || x.AgreementId == lazy.Id) &&
                page.Items.Any(x => x.Id == current.Id && x.ResponsibleName == "" && !x.ResponsibleValid) &&
                page.Owners.All(x => !x.Name.Contains("foreign-secret")), "account deadline counts, options and rows respect grants and sanitize foreign responsible metadata without removing deadline");
            actor.Current = new(true, foreign, a);
            await Denied(() => accountDeadlines.ReadAsync(a, new()), "account deadline reader rejects foreign membership");
            actor.Current = new(true, platform, a);
            Check((await accountDeadlines.ReadAsync(a, new())).Total == 0, "global platform administrator has no automatic deadline visibility");
        }
        finally { guard.Protect = false; actor.Current = new(true, owner, a); }
        var deniedInitialization = new AgreementInitializationService(new AuthorizationService(new Actor(new(true, foreign, a)), new AuthorizationData(factory), clock), new AgreementDeadlineInitializer(factory, clock));
        await Denied(() => deniedInitialization.EnsureAgreementAsync(a, untouched.Id), "direct Application initialization rejects foreign actor");
        var probe = new FollowupProbe(); var probeContext = new WebContext(owner, a);
        var facade = new AgreementService(null!, new ListProbe(), new AttentionProbe(), new DashboardProbe(), new InsightsProbe(), new DetailsProbe(), probe, null!, null!, null!, null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        Check(ReferenceEquals(await facade.GetFollowupAsync(a, target.Id, cancellation.Token), probe.Result) && probe.Account == a && probe.Id == target.Id && probe.Token == cancellation.Token, "Web facade forwards account, resource, cancellation and exact initialized result");
        await using (var db = factory.CreateDbContext()) Check(!await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == a), "all readers and targeted GUI initialization leave absent settings absent");
        var indices = new AgreementIndexService(auth, new AgreementIndexData(factory), clock);
        actor.Current = new(true, editor, a);
        await Denied(() => indices.CreateIndexAsync(a, "I", "Index", "", "Source", AgreementIndexResolution.Month), "direct index writer rejects Edit grant");
        actor.Current = new(true, owner, a); guard.Protect = true;
        try { Check(!(await indices.GetIndicesAsync(a)).CanWrite, "direct index register retains member read without write"); }
        finally { guard.Protect = false; }
        actor.Current = new(true, admin, a);
        if (provider == "PostgreSQL")
        {
            var index = await indices.CreateIndexAsync(a, "  test  ", " Index ", " Description ", " Source ", AgreementIndexResolution.Month);
            await indices.UpdateIndexAsync(a, index, "test", "Updated", "", "Source", AgreementIndexResolution.Month);
            var period = new DateOnly(2026, 10, 1);
            await indices.RecordIndexValueAsync(a, index, period, 100m, null, "Initial");
            var first = (await indices.GetIndicesAsync(a)).Values.Single(x => x.IndexId == index);
            await indices.RecordIndexValueAsync(a, index, period, 101m, null, "Correction", first.Id);
            var register = await indices.GetIndicesAsync(a);
            Check(register.Indices.Single(x => x.Id == index).Code == "TEST" && register.Values.Count(x => x.IndexId == index) == 2 && register.Values.Single(x => x.Id == first.Id).Superseded, "direct index writes preserve normalized register and correction history");
            try { await indices.RecordIndexValueAsync(a, index, period, 102m, null, "Stale", first.Id); throw new Exception("Expected stale correction"); }
            catch (AgreementValidationException ex) when (ex.Message == "ProcessingStale") { Check(true, "direct index rejects replaced revision"); }
            await Denied(() => indices.UpdateIndexAsync(a, Guid.NewGuid(), "x", "x", "", "Source", AgreementIndexResolution.Month), "direct index rejects unknown or foreign index");
        }
        var writer = new AgreementFollowupWriter(auth, new AgreementFollowupWriteData(factory), clock);
        actor.Current = new(true, reader, a);
        await Denied(() => writer.FollowupAsync(a, current.Id, target.Revision, AgreementFollowupAction.Start, null, null), "direct Application writer denies Read grant");
        actor.Current = new(true, editor, a);
        await Denied(() => writer.ArchiveAsync(a, target.Id, target.Revision), "direct Application writer denies Edit archival");
        await Denied(() => writer.SaveReminderSettingsAsync(a, new()), "direct Application settings rejects editor");
        actor.Current = new(true, admin, a); guard.Protect = true;
        try { Check((await writer.GetReminderSettingsAsync(a)).Revision == Guid.Empty, "direct settings read is write free with missing settings"); }
        finally { guard.Protect = false; }
        if (provider == "PostgreSQL")
        {
            var command = Make("Direct commands"); command.Form = AgreementForm.Ongoing; command.NoticeCount = 1; command.NoticeUnit = AgreementNoticeUnit.Months;
            command.EndDate = null; command.NoticeDeadline = null;
            var deadline = new AgreementDeadline { Id = Guid.NewGuid(), AccountId = a, AgreementId = command.Id, Kind = AgreementDeadlineKind.Expiry, DueDate = today.AddDays(10), PeriodStartDate = command.StartDate, CreatedUtc = now, UpdatedUtc = now };
            await using (var db = factory.CreateDbContext()) { db.Agreements.Add(command); db.AgreementDeadlines.Add(deadline); await db.SaveChangesAsync(); }
            async Task<Guid> Revision() { await using var db = factory.CreateDbContext(); return await db.Agreements.Where(x => x.Id == command.Id).Select(x => x.Revision).SingleAsync(); }
            actor.Current = new(true, owner, a);
            await writer.FollowupAsync(a, deadline.Id, await Revision(), AgreementFollowupAction.Start, null, null);
            await writer.FollowupAsync(a, deadline.Id, await Revision(), AgreementFollowupAction.Assign, admin, null);
            await writer.FollowupAsync(a, deadline.Id, await Revision(), AgreementFollowupAction.Complete, null, "Done");
            await writer.FollowupAsync(a, deadline.Id, await Revision(), AgreementFollowupAction.Reopen, null, null);
            await writer.SetReminderPreferencesAsync(a, command.Id, await Revision(), AgreementReminderMode.Disabled, []);
            await writer.RecordTerminationAsync(a, command.Id, new() { Revision = await Revision(), EffectiveDate = today, Action = AgreementNoticeAction.Registered });
            await using (var db = factory.CreateDbContext()) Check(await db.AgreementNoticeHistory.AnyAsync(x => x.AgreementId == command.Id && x.ActorUserId == owner) && await db.AgreementDeadlineHistory.CountAsync(x => x.DeadlineId == deadline.Id) >= 4, "direct commands retain history and trusted actor");
            await writer.ArchiveAsync(a, command.Id, await Revision());
            actor.Current = new(true, admin, a);
            await writer.SaveReminderSettingsAsync(a, new() { Enabled = true, Days = [7, 30], TimeZoneId = "Europe/Oslo" });
            Check((await writer.GetReminderSettingsAsync(a)).Days.SequenceEqual(new[] {30,7}), "direct settings writer normalizes days");
            try { await writer.SaveReminderSettingsAsync(a, new()); throw new Exception("Expected stale revision"); }
            catch (AgreementValidationException ex) when (ex.Message == "AgreementConcurrencyConflict") { Check(true, "direct settings writer enforces revision"); }
        }

    }

    private static async Task RenderPanel(AgreementService service, ICurrentUserContextService context, Guid id, Guid revision, Action<bool, string> check)
    {
        var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").AddOptions();
        services.AddSingleton<IAgreementFollowupService>(service); services.AddSingleton(context); services.AddSingleton<NavigationManager>(new FollowupNavigation());
        await using var provider = services.BuildServiceProvider(); await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<TenantPlatform.Web.Components.Pages.Agreements.AgreementFollowupPanel>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["AgreementId"] = id, ["ParentRevision"] = revision }));
            var html = System.Net.WebUtility.HtmlDecode(result.ToHtmlString());
            check(html.Contains("deadline-") && !html.Contains("transport-token") && !html.Contains("stale-secret") && !html.Contains("foreign-secret"), "existing followup panel renders sanitized reader result and controls");
        });
    }

    public static async Task Snapshot()
    {
        var a = Guid.NewGuid(); var user = Guid.NewGuid(); var id = Guid.NewGuid(); var actor = new Actor(new(true, user, a)); var gate = new FollowupGate();
        var clock = new AttentionClock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero)); using var cancellation = new CancellationTokenSource();
        var service = new AgreementFollowupReader(new AuthorizationService(actor, gate, clock), gate, clock);
        var pending = service.ReadAsync(a, id, cancellation.Token); actor.Current = new(false, Guid.NewGuid(), null); gate.Membership.SetResult(new(true, true, false, [])); await pending;
        if (actor.Reads != 1 || clock.Reads != 1 || gate.Scope?.ListScope.UserId != user || gate.Scope.AgreementId != id || gate.Token != cancellation.Token) throw new Exception("Followup snapshot failed");
        Console.WriteLine("PASS followup reader: one actor/time snapshot and token through shared authorization without HTTP/Blazor");
    }
}
sealed class FollowupProbe : IAgreementFollowupReader
{
    public Guid Account; public Guid Id; public CancellationToken Token;
    public AgreementFollowupDetails Result = new(Guid.Empty, false, false, false, [], [], AgreementReminderMode.Inherit, [], false, [90, 30, 7], "Europe/Oslo", new(8, 0), true);
    public Task<AgreementFollowupDetails> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default) { Account = accountId; Id = agreementId; Token = ct; return Task.FromResult(Result); }
}
sealed class FollowupGate : IAuthorizationData, IAgreementFollowupData
{
    public TaskCompletionSource<AuthorizationSubject> Membership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AgreementReadScope? Scope; public CancellationToken Token;
    public Task<AuthorizationSubject> GetSubjectAsync(Guid u, Guid? a, CancellationToken ct) => Membership.Task;
    public Task<ResourceAccessFacts?> GetResourceAsync(Guid a, Guid u, ResourceKind k, Guid id, DateOnly t, CancellationToken ct) => Task.FromResult<ResourceAccessFacts?>(new(a, OwnerUserId: u));
    public Task<AgreementFollowupDetails> ReadAsync(AgreementReadScope scope, DateTimeOffset referenceUtc, CancellationToken ct) { Scope = scope; Token = ct; return Task.FromResult(new FollowupProbe().Result); }
    public Task<bool> AccountExistsAsync(Guid a, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasAgreementRelationshipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasLeasingOwnershipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
}
sealed class FollowupNavigation : NavigationManager
{
    public FollowupNavigation() => Initialize("http://localhost/", "http://localhost/agreements");
    protected override void NavigateToCore(string uri, bool forceLoad) => throw new Exception("Unexpected navigation: " + uri);
}
