using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;

static class OwnerMetadataChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name)
        {
            if (!ok) throw new Exception("FAIL list owner metadata: " + name);
            Console.WriteLine($"PASS {provider} list owner metadata: {name}");
        }
        var account = Guid.NewGuid(); var foreignAccount = Guid.NewGuid(); var party = Guid.NewGuid(); var foreignParty = Guid.NewGuid();
        var reader = Guid.NewGuid(); var active = Guid.NewGuid(); var inactive = Guid.NewGuid();
        var removed = Guid.NewGuid(); var foreign = Guid.NewGuid(); var hiddenOwner = Guid.NewGuid(); var admin = Guid.NewGuid();
        var owners = new[] { active, inactive, removed, foreign };
        var removedMembership = Guid.NewGuid();
        var clock = new Clock(new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
        Agreement Make(string title, Guid owner, bool abroad = false) => new()
        {
            Id = Guid.NewGuid(), AccountId = abroad ? foreignAccount : account, Title = title, OwnerUserId = owner,
            CounterpartyOrganizationId = abroad ? foreignParty : party, Type = AgreementType.Other, Status = AgreementStatus.Active,
            StartDate = new(2020, 1, 1), EndDate = new(2027, 1, 1), NoticeDeadline = new(2026, 10, 17),
            Form = AgreementForm.Legacy, CreatedUtc = clock.GetUtcNow(), UpdatedUtc = clock.GetUtcNow(),
            CreatedByUserId = owner, UpdatedByUserId = owner, Revision = Guid.NewGuid()
        };
        var rows = owners.Select((owner, i) => Make("Metadata " + i, owner))
            .Concat(Enumerable.Range(0, 30).Select(i => Make("Paging", owners[i % owners.Length]))).ToArray();
        rows[1].Type = AgreementType.Lease; rows[1].Status = AgreementStatus.Draft;
        rows[2].IsArchived = true;
        var hidden = Make("Hidden secret", hiddenOwner); var foreignRow = Make("Foreign secret", foreign, true);
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Owner metadata A" }, new Account { Id = foreignAccount, Name = "Owner metadata B" });
            foreach (var id in owners.Concat([reader, hiddenOwner, admin]))
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = id == foreign ? "foreign-secret" : id == removed ? "removed-secret" : id == inactive ? "Inactive" : "Member", LastName = "Owner", IsActive = id != inactive });
                var membership = new UserAccount { Id = id == removed ? removedMembership : Guid.NewGuid(), AccountId = id == foreign ? foreignAccount : account, UserId = id };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Organizations.AddRange(new Organization { Id = party, AccountId = account, Name = "Metadata counterparty" }, new Organization { Id = foreignParty, AccountId = foreignAccount, Name = "Foreign party" });
            db.Agreements.AddRange(rows.Concat([hidden, foreignRow]));
            db.AgreementAccess.AddRange(rows.Select((row, i) => new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = row.Id, UserId = reader, Level = i % 2 == 0 ? AgreementAccessLevel.Read : AgreementAccessLevel.Edit }));
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, reader, account));
        var service = new AgreementListService(new AuthorizationService(actor, new AuthorizationData(factory), clock), new AgreementListData(factory), clock);
        async Task<AgreementPageDto> List(AgreementFilter? filter = null)
        {
            guard.Protect = true;
            try { return await service.ListAsync(account, filter ?? new()); }
            finally { guard.Protect = false; }
        }
        var firstBefore = await List(); var secondBefore = await List(new() { Page = 2 });
        Check(firstBefore.Owners.Select(x => x.Id).ToHashSet().SetEquals([active, inactive, removed]), "options use current-account membership, including inactive member, excluding foreign and ungranted owners");
        await using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == removedMembership));
            await db.SaveChangesAsync();
        }
        // Snapshot the business records after the deliberate membership change; all reads below must preserve them.
        async Task<string> State()
        {
            await using var db = factory.CreateDbContext();
            return JsonSerializer.Serialize(new
            {
                Agreements = await db.Agreements.AsNoTracking().Where(x => x.AccountId == account || x.AccountId == foreignAccount).OrderBy(x => x.Id).ToListAsync(),
                Grants = await db.AgreementAccess.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Id).ToListAsync(),
                Settings = await db.AgreementReminderSettings.CountAsync(x => x.AccountId == account),
                Deadlines = await db.AgreementDeadlines.CountAsync(x => x.AccountId == account)
            });
        }
        var state = await State();
        var first = await List(); var second = await List(new() { Page = 999 });
        var all = first.Items.Concat(second.Items).ToArray();
        Check(first.TotalCount == rows.Length && second.TotalCount == rows.Length && first.PageSize == 25 && first.Page == 1 && second.Page == 2 && first.Items.Count == 25 && second.Items.Count == 9, "count, page size and clamping preserve all authorized rows");
        Check(first.Items.Select(x => x.Id).SequenceEqual(firstBefore.Items.Select(x => x.Id)) && second.Items.Select(x => x.Id).SequenceEqual(secondBefore.Items.Select(x => x.Id)), "membership removal changes labels only, preserving page boundaries and stable order");
        Check(all.Select(x => x.Id).SequenceEqual(rows.OrderBy(x => x.Title).ThenBy(x => x.Id).Select(x => x.Id)), "Title/ID sorting unchanged with empty owner labels");
        Check(all.Where(x => x.OwnerUserId == active).All(x => x.OwnerName == "Member Owner") && all.Where(x => x.OwnerUserId == inactive).All(x => x.OwnerName == "Inactive Owner"), "active and inactive current-account members retain names");
        Check(all.Where(x => x.OwnerUserId == removed || x.OwnerUserId == foreign).All(x => x.OwnerName == ""), "missing membership and foreign-only membership produce empty labels without removing rows or owner IDs");
        Check(first.Owners.Select(x => x.Id).ToHashSet().SetEquals([active, inactive]) && first.Owners.All(x => !x.Name.Contains("secret")), "revoked membership is rechecked and options contain no foreign/missing labels");
        foreach (var owner in owners)
        {
            var filtered = await List(new() { OwnerUserId = owner });
            Check(filtered.TotalCount == rows.Count(x => x.OwnerUserId == owner) && filtered.Items.Select(x => x.Id).ToHashSet().SetEquals(rows.Where(x => x.OwnerUserId == owner).Select(x => x.Id)), "owner ID filtering preserves access even without label: " + owner);
        }
        var searched = await List(new() { Search = " METADATA 2 " });
        Check(searched.TotalCount == 1 && searched.Items.Single().Id == rows[2].Id && searched.Items.Single().IsArchived && searched.Owners.Count == first.Owners.Count, "search/archive semantics and owner options before search are unchanged");
        Check((await List(new() { Search = "COUNTERPARTY" })).TotalCount == rows.Length && (await List(new() { Search = "foreign-secret" })).TotalCount == 0, "search still uses title/counterparty, not owner labels");
        var typed = await List(new() { Type = AgreementType.Lease, Status = AgreementStatus.Draft, OwnerUserId = inactive });
        Check(typed.TotalCount == 1 && typed.Items.Single().Id == rows[1].Id, "combined type/status/owner filters retain inactive-owner row");
        Check((await List(new() { OwnerUserId = hiddenOwner })).TotalCount == 0 && (await List(new() { Search = "secret" })).TotalCount == 0, "filters do not widen grants or account boundary");
        actor.Current = new(true, admin, account);
        Check((await List()).TotalCount == rows.Length + 1 && (await List(new() { OwnerUserId = foreign })).TotalCount == rows.Count(x => x.OwnerUserId == foreign), "account admin retains rows with foreign-only owner but no foreign-account agreements");
        Check(await State() == state, "read-only business rows, ownership, grants, revisions, settings and deadlines; SaveChanges guard on every Application call");
    }
}
