using Microsoft.EntityFrameworkCore;
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
using TenantPlatform.Web.Services.Agreements;

static class DashboardChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL prioritized: " + name); Console.WriteLine($"PASS {provider} prioritized: {name}"); }
        var account = Guid.NewGuid(); var foreignAccount = Guid.NewGuid();
        var user = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid(); var platform = Guid.NewGuid();
        var party = Guid.NewGuid(); var foreignParty = Guid.NewGuid(); var memberId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 17);
        var clock = new Clock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero));
        Agreement Make(string title, int? end, int? notice = null, AgreementStatus status = AgreementStatus.Active, bool archived = false, bool isForeign = false, Guid? owner = null) => new()
        {
            Id = Guid.NewGuid(), AccountId = isForeign ? foreignAccount : account, OwnerUserId = owner ?? user,
            CounterpartyOrganizationId = isForeign ? foreignParty : party, Title = title, Type = AgreementType.Other,
            Status = status, IsArchived = archived, StartDate = today.AddYears(-1),
            EndDate = end.HasValue ? today.AddDays(end.Value) : null, NoticeDeadline = notice.HasValue ? today.AddDays(notice.Value) : null,
            CreatedUtc = clock.GetUtcNow(), UpdatedUtc = clock.GetUtcNow(), CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid()
        };
        var alpha = Make("Alpha", -1); var overlap = Make("Zulu", -1, 0, owner: other);
        var old = Make("Older", -20, 0, owner: other); var expiredStatus = Make("Expired status", -2, status: AgreementStatus.Expired);
        var endToday = Make("A today", 0, 0); var noEnd = Make("B no end", null, 0);
        var ties = new[] { Make("Tie", 100, 1), Make("Tie", 100, 1) }.OrderBy(x => x.Id).ToArray();
        var later = Make("Later", 100, 2); var day90 = Make("Day90", 100, 90); var day91 = Make("Day91", 100, 91);
        var renewalOnly = Make("Renewal only", 100); renewalOnly.RenewalDate = today;
        var excluded = new[] { day91, renewalOnly, Make("Missing", null), Make("Past notice", 100, -1),
            Make("Ends today only", 0), Make("Draft", -1, 0, AgreementStatus.Draft), Make("Terminated", -1, 0, AgreementStatus.Terminated),
            Make("Archived", -1, 0, archived: true), Make("Expired future", 1, 0, AgreementStatus.Expired) };
        var hidden = Enumerable.Range(0, 6).Select(i => Make("000 Hidden " + i, -1, 0, owner: other)).ToArray();
        var foreign = Enumerable.Range(0, 6).Select(i => Make("000 Foreign " + i, -1, 0, isForeign: true, owner: other)).ToArray();
        var first = new[] { alpha, overlap, expiredStatus, old };
        var notices = new[] { endToday, noEnd, ties[0], ties[1], later };
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Priority A" }, new Account { Id = foreignAccount, Name = "Priority B" });
            foreach (var id in new[] { user, other, admin, platform })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = "Priority", LastName = id.ToString(), IsActive = true, IsPlatformAdmin = id == platform });
                var membership = new UserAccount { Id = id == user ? memberId : Guid.NewGuid(), UserId = id, AccountId = account };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.UserAccounts.Add(new() { Id = Guid.NewGuid(), AccountId = foreignAccount, UserId = other });
            db.Organizations.AddRange(new Organization { Id = party, AccountId = account, Name = "Priority party" }, new Organization { Id = foreignParty, AccountId = foreignAccount, Name = "Foreign priority party" });
            db.Agreements.AddRange(first.Concat(notices).Append(day90).Concat(excluded).Concat(hidden).Concat(foreign));
            db.AgreementAccess.AddRange(new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = old.Id, UserId = user, Level = AgreementAccessLevel.Read },
                new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = overlap.Id, UserId = user, Level = AgreementAccessLevel.Edit });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, user, account));
        var auth = new AuthorizationService(actor, new AuthorizationData(factory), clock);
        var service = new AgreementDashboardService(auth, new AgreementDashboardData(factory), clock);
        async Task<AgreementDashboardList> Read()
        {
            guard.Protect = true;
            try { return await service.GetDashboardContractsAsync(account); }
            finally { guard.Protect = false; }
        }
        async Task Denied(string name)
        {
            try { await Read(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        var baseline = await Read();
        Check(baseline.Today == today && baseline.Items.Select(x => x.Id).SequenceEqual(first.Append(endToday).Select(x => x.Id)), "owner/Read/Edit, exact expiry/notice ordering, limit five; hidden/foreign rows cannot displace authorized rows");
        Check(baseline.Items.Select(x => x.Id).Distinct().Count() == 5 && baseline.Items.Count(x => x.Id == overlap.Id) == 1, "Expired/Notice union deduplicates overlap");
        Check(baseline.Items[0] == new AgreementDashboardItem(alpha.Id, alpha.Title, alpha.Type, alpha.EndDate), "DTO preserves detail-link ID, title, type and end date");
        actor.Current = new(true, admin, account);
        Check((await Read()).Items.Select(x => x.Id).SequenceEqual(hidden.Take(5).Select(x => x.Id)), "AccountAdmin sees account rows; foreign high-priority rows excluded before limit");
        actor.Current = new(true, platform, account); Check((await Read()).Items.Count == 0, "PlatformAdmin with membership but no agreement access has empty authorized result");
        actor.Current = new(true, platform, foreignAccount);
        try { await service.GetDashboardContractsAsync(foreignAccount); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { Check(true, "PlatformAdmin without membership denied"); }
        actor.Current = new(false, user, account); await Denied("missing authentication");
        actor.Current = new(true, user, null); await Denied("missing selected account");
        actor.Current = new(true, user, foreignAccount); await Denied("wrong account argument");
        actor.Current = new(true, Guid.NewGuid(), account); await Denied("missing user/membership");
        actor.Current = new(true, user, account);
        await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == user)).IsActive = false; await db.SaveChangesAsync(); }
        await Denied("inactive user despite ownership/grants");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == user)).IsActive = true;
            foreach (var id in first.Select(x => x.Id)) (await db.Agreements.SingleAsync(x => x.Id == id)).IsArchived = true;
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 3; i++) Check((await Read()).Items.Select(x => x.Id).SequenceEqual(notices.Select(x => x.Id)), "notice order, missing end/end today, title and stable ID ties before Take(5), run " + i);
        await using (var db = factory.CreateDbContext())
        {
            foreach (var id in notices.Select(x => x.Id)) (await db.Agreements.SingleAsync(x => x.Id == id)).IsArchived = true;
            await db.SaveChangesAsync();
        }
        Check((await Read()).Items.Select(x => x.Id).SequenceEqual(new[] { day90.Id }), "day90 inclusive; day91, Renewal-only, null dates, past notice, ineligible status and archive excluded");
        await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Add(new() { AccountId = account, TimeZoneId = "UTC" }); await db.SaveChangesAsync(); }
        var utc = await Read(); Check(utc.Today == today.AddDays(-1) && utc.Items.Select(x => x.Id).SequenceEqual(new[] { excluded.Single(x => x.Title == "Past notice").Id }), "stored UTC shifts midnight: past notice becomes today, day90 moves beyond horizon");
        await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Remove(await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == account)); await db.SaveChangesAsync(); }
        foreach (var instant in new[] { new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), new(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 1, 30, 0, TimeSpan.Zero) })
        {
            var changingClock = new AttentionClock(instant);
            var dstService = new AgreementDashboardService(auth, new AgreementDashboardData(factory), changingClock);
            guard.Protect = true;
            AgreementDashboardList dst;
            try { dst = await dstService.GetDashboardContractsAsync(account); }
            finally { guard.Protect = false; }
            var expected = instant.Month == 3 ? Array.Empty<Guid>() : new[] { excluded.Single(x => x.Title == "Expired future").Id, excluded.Single(x => x.Title == "Ends today only").Id, day90.Id, day91.Id };
            Check(dst.Today == DateOnly.FromDateTime(instant.UtcDateTime) && changingClock.Reads == 1 && dst.Items.Select(x => x.Id).SequenceEqual(expected), "DST calendar date, selection and single reference instant: " + instant.ToString("O"));
        }
        await using (var db = factory.CreateDbContext())
        {
            foreach (var id in first.Concat(notices).Select(x => x.Id)) (await db.Agreements.SingleAsync(x => x.Id == id)).IsArchived = false;
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == account && x.AgreementId == old.Id && x.UserId == user)); await db.SaveChangesAsync();
        }
        Check((await Read()).Items.Select(x => x.Id).SequenceEqual(new[] { alpha.Id, overlap.Id, expiredStatus.Id, endToday.Id, noEnd.Id }), "same instance observes revoked Read grant and fills freed place");
        await using (var db = factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == memberId)); await db.SaveChangesAsync(); }
        await Denied("same instance observes removed membership");
        await using (var db = factory.CreateDbContext()) Check(!await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == account) && !await db.AgreementDeadlines.AnyAsync(x => x.AccountId == account), "missing settings/deadlines stay absent; SaveChanges blocked during every read");
        var probe = new DashboardProbe(); var context = new WebContext(user, account);
        var facade = new AgreementService(null!, new ListProbe(), new AttentionProbe(), probe, new InsightsProbe(), new DetailsProbe(), new FollowupProbe(), null!, null!, null!, null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        var forwarded = await facade.GetDashboardContractsAsync(account, cancellation.Token);
        Check(ReferenceEquals(forwarded, probe.Result) && probe.Account == account && probe.Token == cancellation.Token, "Web facade forwards account, cancellation and exact result");
    }

    public static async Task Snapshot()
    {
        var account = Guid.NewGuid(); var user = Guid.NewGuid(); var actor = new Actor(new(true, user, account)); var gate = new GateData();
        var clock = new AttentionClock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero));
        var service = new AgreementDashboardService(new AuthorizationService(actor, gate, clock), gate, clock);
        var pending = service.GetDashboardContractsAsync(account);
        actor.Current = new(false, Guid.NewGuid(), null); gate.Membership.SetResult(new(true, true, false, []));
        var result = await pending;
        if (actor.Reads != 1 || clock.Reads != 1 || gate.Scope?.UserId != user || result.Today != new DateOnly(2026, 10, 17)) throw new Exception("Priority snapshot failed");
        Console.WriteLine("PASS prioritized: immutable actor and single time snapshot across await without HTTP/Blazor");
    }
}
sealed class DashboardProbe : IAgreementDashboardService
{
    public Guid Account; public CancellationToken Token;
    public AgreementDashboardList Result = new(new(2026, 10, 17), []);
    public Task<AgreementDashboardList> GetDashboardContractsAsync(Guid accountId, CancellationToken ct = default) { Account = accountId; Token = ct; return Task.FromResult(Result); }
}
