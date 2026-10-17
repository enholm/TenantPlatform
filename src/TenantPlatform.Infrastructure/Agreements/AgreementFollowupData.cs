using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementFollowupData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementFollowupData
{
    public async Task<AgreementFollowupDetails> ReadAsync(AgreementReadScope scope, DateTimeOffset referenceUtc, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var accountId = scope.ListScope.AccountId;
        var agreementId = scope.AgreementId;
        var a = await AgreementReadQueries.Visible(db, scope.ListScope).SingleOrDefaultAsync(x => x.Id == agreementId, ct)
            ?? throw new UnauthorizedAccessException();
        var settings = await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, ct)
            ?? new AgreementReminderSettings { AccountId = accountId };
        var today = AgreementReminderSchedule.Today(referenceUtc, settings.TimeZoneId);
        var editors = await AgreementFollowupAccess.Editors(db, accountId, a).AsNoTracking()
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.Id)
            .Select(x => new AgreementOptionDto(x.Id, x.FirstName + " " + x.LastName, null)).ToListAsync(ct);
        var editorIds = editors.Select(x => x.Id).ToHashSet();
        var members = db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id));
        var deadlineQuery = db.AgreementDeadlines.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId);
        var deadlines = await (from d in deadlineQuery
            join u in members on (d.AssignedUserId ?? a.OwnerUserId) equals u.Id into responsible
            from u in responsible.DefaultIfEmpty()
            orderby d.State, d.DueDate, d.Id
            select new { Deadline = d, Name = u == null ? string.Empty : u.FirstName + " " + u.LastName }).ToListAsync(ct);
        var party = await db.Organizations.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == a.CounterpartyOrganizationId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct) ?? string.Empty;
        // Batch all related rows, joining to scoped deadlines before materialization; no per-deadline I/O.
        var history = await (from h in db.AgreementDeadlineHistory.AsNoTracking()
            join d in deadlineQuery on h.DeadlineId equals d.Id
            join actor in members on h.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            join assignee in members on h.AssignedUserId equals assignee.Id into assignees
            from assignee in assignees.DefaultIfEmpty()
            where h.AccountId == accountId
            orderby h.CreatedUtc, h.Id
            select new { h.DeadlineId, Value = new AgreementHistoryDto(h.Id, h.Kind, h.CreatedUtc,
                h.ActorUserId == null ? null : actor == null ? string.Empty : actor.FirstName + " " + actor.LastName,
                h.AssignedUserId == null ? null : assignee == null ? string.Empty : assignee.FirstName + " " + assignee.LastName, h.Comment) }).ToListAsync(ct);
        var historyByDeadline = history.ToLookup(x => x.DeadlineId, x => x.Value);
        var summaries = new List<(Guid DeadlineId, AgreementReminderDto Value)>();
        if (AgreementFollowupRules.CanInspectDelivery(scope))
        {
            var reminders = await (from r in db.AgreementReminders.AsNoTracking()
                join d in deadlineQuery on r.DeadlineId equals d.Id
                join recipient in members on r.RecipientUserId equals recipient.Id into recipients
                from recipient in recipients.DefaultIfEmpty()
                where r.AccountId == accountId
                orderby r.ScheduledUtc, r.Id
                select new { r.DeadlineId, r.DaysBefore, r.Status, Recipient = recipient == null ? null : recipient.Email,
                    r.ScheduledUtc, r.Attempts, r.LastAttemptUtc, r.SentUtc, r.ResultKey }).ToListAsync(ct);
            summaries = reminders.Select(x => (x.DeadlineId, new AgreementReminderDto(x.DaysBefore, x.Status, x.Recipient,
                x.ScheduledUtc, x.Attempts, x.LastAttemptUtc, x.SentUtc, AgreementFollowupRules.PublicResultKey(x.ResultKey)))).ToList();
        }
        var remindersByDeadline = summaries.ToLookup(x => x.DeadlineId, x => x.Value);
        var details = deadlines.Select(row =>
        {
            var d = row.Deadline;
            var responsibleId = d.AssignedUserId ?? a.OwnerUserId;
            return new AgreementDeadlineDetail(new(d.Id, a.Id, a.Title, party, a.Type, d.Kind, d.DueDate,
                d.DueDate.DayNumber - today.DayNumber, responsibleId, row.Name, editorIds.Contains(responsibleId),
                d.Status, d.State, d.UpdatedUtc), d.AssignedUserId, historyByDeadline[d.Id].ToList(), remindersByDeadline[d.Id].ToList());
        }).ToList();
        return new(a.Revision, scope.CanEdit && !a.IsArchived, scope.CanManageAccess && !a.IsArchived, a.IsArchived,
            details, scope.CanEdit && !a.IsArchived ? editors : [], a.ReminderMode, a.ReminderDays,
            settings.Enabled, settings.Days, settings.TimeZoneId, settings.SendAt, a.DeadlinesInitialized);
    }
}
