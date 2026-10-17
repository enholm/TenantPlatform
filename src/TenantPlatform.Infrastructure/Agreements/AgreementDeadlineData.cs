using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementDeadlineData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementDeadlineData
{
    public async Task<string> ReadTimeZoneAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.ReminderTimeZoneAsync(db, accountId, ct);
    }
    public async Task<AgreementDeadlinePage> QueryAsync(AgreementListScope scope, AgreementDeadlineQuery filter, DateOnly today, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var accountId = scope.AccountId;
        var userId = scope.UserId;
        var visible = AgreementReadQueries.Visible(db, scope);
        var raw = from d in db.AgreementDeadlines.AsNoTracking()
            join a in visible on d.AgreementId equals a.Id
            join orgRow in db.Organizations.Where(x => x.AccountId == accountId) on a.CounterpartyOrganizationId equals orgRow.Id into parties
            from org in parties.DefaultIfEmpty()
            join ownerRow in db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id)) on (d.AssignedUserId ?? a.OwnerUserId) equals ownerRow.Id into members
            from owner in members.DefaultIfEmpty()
            where d.AccountId == accountId
            select new { Deadline = d, Agreement = a, Counterparty = org == null ? "" : org.Name, OwnerId = d.AssignedUserId ?? a.OwnerUserId,
                OwnerName = owner == null ? "" : owner.FirstName + " " + owner.LastName,
                Responsible = new AgreementResponsibleFacts { AgreementId = a.Id, UserId = d.AssignedUserId ?? a.OwnerUserId,
                    IsActive = owner != null && owner.IsActive,
                    IsMember = owner != null && db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == owner.Id),
                    IsOwner = owner != null && owner.Id == a.OwnerUserId,
                    IsAccountAdmin = owner != null && db.UserAccountRoles.Any(r => r.UserAccount.AccountId == accountId && r.UserAccount.UserId == owner.Id && r.Role == UserRole.AccountAdmin),
                    HasEditGrant = owner != null && db.AgreementAccess.Any(g => g.AccountId == accountId && g.AgreementId == a.Id && g.UserId == owner.Id && g.Level == AgreementAccessLevel.Edit) } };
        var eligible = raw.Select(x => x.Responsible).Where(AgreementResponsibleRules.Eligible);
        var query = raw.Select(x => new { x.Deadline, x.Agreement, x.Counterparty, x.OwnerId, x.OwnerName,
            Valid = eligible.Any(candidate => candidate.AgreementId == x.Agreement.Id && candidate.UserId == x.OwnerId) });

        var open = query.Where(x => !x.Agreement.IsArchived && x.Deadline.State == AgreementDeadlineState.Current &&
            x.Deadline.Status != AgreementFollowupStatus.Completed);
        var overdue = await open.CountAsync(x => x.Deadline.DueDate < today, ct);
        var next30 = await open.CountAsync(x => x.Deadline.DueDate >= today && x.Deadline.DueDate <= today.AddDays(30), ct);
        var missing = await open.CountAsync(x => !x.Valid, ct);
        var owners = await query.Where(x => x.OwnerName != "").Select(x => new { x.OwnerId, x.OwnerName }).Distinct().OrderBy(x => x.OwnerName)
            .Select(x => new AgreementOptionDto(x.OwnerId, x.OwnerName, null)).ToListAsync(ct);
        if (filter.State.HasValue) query = query.Where(x => x.Deadline.State == filter.State);
        if (filter.Status.HasValue) query = query.Where(x => x.Deadline.Status == filter.Status);
        else if (!filter.IncludeCompleted) query = query.Where(x => x.Deadline.Status != AgreementFollowupStatus.Completed);
        if (filter.Mine) query = query.Where(x => x.OwnerId == userId);
        if (filter.AssignedUserId.HasValue) query = query.Where(x => x.OwnerId == filter.AssignedUserId);
        if (filter.AgreementType.HasValue) query = query.Where(x => x.Agreement.Type == filter.AgreementType);
        if (filter.Kind.HasValue) query = query.Where(x => x.Deadline.Kind == filter.Kind);
        if (filter.CustomDates)
        {
            if (filter.From.HasValue) query = query.Where(x => x.Deadline.DueDate >= filter.From);
            if (filter.To.HasValue) query = query.Where(x => x.Deadline.DueDate <= filter.To);
        }
        else
        {
            var until = today.AddDays(filter.HorizonDays);
            query = query.Where(x => x.Deadline.DueDate <= until); // Deliberately no lower bound: includes overdue.
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Agreement.Title.ToLower().Contains(search) || x.Counterparty.ToLower().Contains(search));
        }
        var count = await query.CountAsync(ct);
        const int size = 25;
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (count + size - 1) / size));
        var rows = await query.OrderBy(x => x.Deadline.DueDate).ThenBy(x => x.Deadline.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(x => new AgreementDeadlineRow(x.Deadline.Id, x.Agreement.Id, x.Agreement.Title,
            x.Counterparty, x.Agreement.Type, x.Deadline.Kind, x.Deadline.DueDate, x.Deadline.DueDate.DayNumber - today.DayNumber,
            x.OwnerId, x.OwnerName, x.Valid, x.Deadline.Status, x.Deadline.State, x.Deadline.UpdatedUtc)).ToList(),
            count, page, size, owners, overdue, next30, missing);
    }
}
