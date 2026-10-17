using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Agreements;

static class AttentionChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, Guid account, Guid foreignAccount,
        Guid user, Guid admin, Guid platform, Guid memberId, Guid readId, Guid party, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL attention: " + name); Console.WriteLine($"PASS {provider} attention: {name}"); }
        var actor = new Actor(new(true, user, account));
        var clock = new Clock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero));
        var auth = new AuthorizationService(actor, new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), clock);
        var attention = new AgreementAttentionService(auth, new AgreementAttentionData(factory), clock);
        var list = new AgreementListService(auth, new AgreementListData(factory), clock);
        async Task<AgreementAttentionDto> Read()
        {
            guard.Protect = true;
            try { return await attention.GetAttentionAsync(account); }
            finally { guard.Protect = false; }
        }
        async Task Denied(string name)
        {
            try { await Read(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        var baseline = await Read();
        Check(baseline == new AgreementAttentionDto(new(2026, 10, 17), 5, 6, 1), "owner + Read/Edit grants; hidden and foreign agreements excluded from every category");
        async Task MatchesList()
        {
            var counts = await Read();
            foreach (var kind in Enum.GetValues<AgreementAttention>())
            {
                guard.Protect = true;
                AgreementPageDto page;
                try { page = await list.ListAsync(account, new() { Attention = kind }); }
                finally { guard.Protect = false; }
                var count = kind switch { AgreementAttention.Notice => counts.Notice, AgreementAttention.Renewal => counts.Renewal, _ => counts.Expired };
                Check(page.TotalCount == count, "dashboard equals filtered list TotalCount for " + kind);
            }
        }
        await MatchesList();
        actor.Current = new(true, admin, account);
        Check(await Read() == new AgreementAttentionDto(baseline.Today, 34, 35, 1), "AccountAdmin counts all authorized account agreements only");
        await MatchesList();
        actor.Current = new(true, platform, account);
        Check(await Read() == new AgreementAttentionDto(baseline.Today, 0, 0, 0), "global PlatformAdmin without account grants sees empty counts");
        actor.Current = new(true, platform, foreignAccount);
        try { await attention.GetAttentionAsync(foreignAccount); throw new Exception("Expected nonmember denial"); } catch (UnauthorizedAccessException) { Check(true, "PlatformAdmin cannot bypass membership"); }
        actor.Current = new(false, user, account); await Denied("missing authentication");
        actor.Current = new(true, user, null); await Denied("missing selected account");
        actor.Current = new(true, user, foreignAccount); await Denied("forged account argument");
        actor.Current = new(true, Guid.NewGuid(), account); await Denied("missing user/membership");
        actor.Current = new(true, user, account);
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == user)).IsActive = false; await db.SaveChangesAsync();
        }
        await Denied("inactive user despite ownership and grants");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == user)).IsActive = true;
            db.AgreementReminderSettings.Add(new() { AccountId = account, TimeZoneId = "UTC" }); await db.SaveChangesAsync();
        }
        Check(await Read() == new AgreementAttentionDto(new(2026, 10, 16), 4, 0, 0), "saved UTC versus default Oslo at midnight changes inclusive horizon and strict expiry");
        await MatchesList();
        await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Remove(await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == account)); await db.SaveChangesAsync(); }
        var today = baseline.Today;
        Agreement Make(string name, AgreementStatus status = AgreementStatus.Active, bool archived = false, int? notice = 0, int? renewal = 0, int? end = -1) => new()
        {
            Id = Guid.NewGuid(), AccountId = account, Title = "Attention " + name, CounterpartyOrganizationId = party,
            OwnerUserId = user, Type = AgreementType.Other, Status = status, IsArchived = archived, StartDate = today.AddYears(-1),
            NoticeDeadline = notice.HasValue ? today.AddDays(notice.Value) : null,
            RenewalDate = renewal.HasValue ? today.AddDays(renewal.Value) : null, EndDate = end.HasValue ? today.AddDays(end.Value) : null,
            CreatedUtc = clock.GetUtcNow(), UpdatedUtc = clock.GetUtcNow(), CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid()
        };
        var extra = new[] { Make("end today", end: 0), Make("expired status", AgreementStatus.Expired),
            Make("draft", AgreementStatus.Draft), Make("terminated", AgreementStatus.Terminated), Make("archived", archived: true),
            Make("missing", notice: null, renewal: null, end: null), Make("past notice", notice: -1, renewal: -1, end: 100),
            Make("overlap"), Make("day 91", notice: 91, renewal: 91, end: 100) };
        await using (var db = factory.CreateDbContext()) { db.Agreements.AddRange(extra); await db.SaveChangesAsync(); }
        Check(await Read() == new AgreementAttentionDto(today, baseline.Notice + 2, baseline.Renewal + 2, baseline.Expired + 2), "status/archive/null/past/day91/end-today/overlap matrix; categories count independently");
        await MatchesList();
        await using (var db = factory.CreateDbContext()) { db.Agreements.RemoveRange(extra); await db.SaveChangesAsync(); }
        foreach (var instant in new[] { new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), new(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), new(2026, 10, 25, 1, 30, 0, TimeSpan.Zero) })
        {
            var changingClock = new AttentionClock(instant);
            var dst = new AgreementAttentionService(auth, new AgreementAttentionData(factory), changingClock);
            guard.Protect = true;
            AgreementAttentionDto result;
            try { result = await dst.GetAttentionAsync(account); }
            finally { guard.Protect = false; }
            Check(result.Today == DateOnly.FromDateTime(instant.UtcDateTime) && changingClock.Reads == 1, "DST spring/fall uses one reference instant: " + instant.ToString("O"));
        }
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == account && x.AgreementId == readId && x.UserId == user)); await db.SaveChangesAsync();
        }
        Check(await Read() == new AgreementAttentionDto(today, 4, 5, 1), "same Application instance observes grant revocation on next call");
        await MatchesList();
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = readId, UserId = user, Level = AgreementAccessLevel.Read });
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == memberId)); await db.SaveChangesAsync();
        }
        await Denied("same Application instance observes revoked membership");
        await using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Add(new() { Id = memberId, AccountId = account, UserId = user }); await db.SaveChangesAsync();
        }
        await using (var db = factory.CreateDbContext())
            Check(!await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == account) && !await db.AgreementDeadlines.AnyAsync(x => x.AccountId == account), "reading missing settings/deadlines never initializes them; SaveChanges blocked during every read");
        var probe = new AttentionProbe(); var context = new WebContext(user, account);
        var facade = new AgreementService(null!, new ListProbe(), probe, new DashboardProbe(), new InsightsProbe(), new DetailsProbe(), new FollowupProbe(), null!, null!, null!, null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        var forwarded = await facade.GetAttentionAsync(account, cancellation.Token);
        Check(ReferenceEquals(forwarded, probe.Result) && probe.Account == account && probe.Token == cancellation.Token, "Web facade delegates account, token and exact result");
    }

    public static async Task Snapshot()
    {
        var account = Guid.NewGuid(); var user = Guid.NewGuid(); var actor = new Actor(new(true, user, account)); var gate = new GateData();
        var clock = new AttentionClock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero));
        var service = new AgreementAttentionService(new AuthorizationService(actor, gate, clock), gate, clock);
        var pending = service.GetAttentionAsync(account);
        actor.Current = new(false, Guid.NewGuid(), null);
        gate.Membership.SetResult(new(true, true, false, []));
        var result = await pending;
        if (actor.Reads != 1 || clock.Reads != 1 || gate.Scope?.UserId != user || result.Today != new DateOnly(2026, 10, 17)) throw new Exception("Attention snapshot failed");
        Console.WriteLine("PASS attention: immutable actor and single reference instant across await; no HTTP or Blazor");
    }
}
sealed class AttentionClock(DateTimeOffset instant) : TimeProvider
{
    public int Reads;
    public override DateTimeOffset GetUtcNow() => instant.AddDays(Reads++);
}
sealed class AttentionProbe : IAgreementAttentionService
{
    public Guid Account; public CancellationToken Token;
    public AgreementAttentionDto Result = new(new(2026, 10, 17), 123, 456, 789);
    public Task<AgreementAttentionDto> GetAttentionAsync(Guid accountId, CancellationToken ct = default) { Account = accountId; Token = ct; return Task.FromResult(Result); }
}
