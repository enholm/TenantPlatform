using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<AgreementDeadlinePage> ListDeadlinesAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        await new AgreementDeadlineInitializer(factory, Clock).EnsureAccountAsync(accountId, ct);
        var settings = await SettingsAsync(db, accountId, ct);
        var today = AgreementReminderSchedule.Today(Clock.GetUtcNow(), settings.TimeZoneId);
        var visible = Accessible(db, accountId, userId, admin).AsNoTracking();
        var query = from d in db.AgreementDeadlines.AsNoTracking()
            join a in visible on d.AgreementId equals a.Id
            join org in db.Organizations on a.CounterpartyOrganizationId equals org.Id
            join owner in db.Users on (d.AssignedUserId ?? a.OwnerUserId) equals owner.Id
            where d.AccountId == accountId && org.AccountId == accountId
            select new { Deadline = d, Agreement = a, Counterparty = org.Name, OwnerId = owner.Id,
                OwnerName = owner.FirstName + " " + owner.LastName,
                Valid = owner.IsActive && db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == owner.Id) &&
                    (owner.Id == a.OwnerUserId || db.UserAccountRoles.Any(r => r.UserAccount.AccountId == accountId &&
                        r.UserAccount.UserId == owner.Id && r.Role == UserRole.AccountAdmin) ||
                     db.AgreementAccess.Any(g => g.AccountId == accountId && g.AgreementId == a.Id &&
                        g.UserId == owner.Id && g.Level == AgreementAccessLevel.Edit)) };
        var open = query.Where(x => !x.Agreement.IsArchived && x.Deadline.State == AgreementDeadlineState.Current &&
            x.Deadline.Status != AgreementFollowupStatus.Completed);
        var overdue = await open.CountAsync(x => x.Deadline.DueDate < today, ct);
        var next30 = await open.CountAsync(x => x.Deadline.DueDate >= today && x.Deadline.DueDate <= today.AddDays(30), ct);
        var missing = await open.CountAsync(x => !x.Valid, ct);
        var owners = await query.Select(x => new { x.OwnerId, x.OwnerName }).Distinct().OrderBy(x => x.OwnerName)
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
            if (filter.From > filter.To) throw new AgreementValidationException("FollowupInvalidRange");
            if (filter.From.HasValue) query = query.Where(x => x.Deadline.DueDate >= filter.From);
            if (filter.To.HasValue) query = query.Where(x => x.Deadline.DueDate <= filter.To);
        }
        else
        {
            if (filter.HorizonDays is not (30 or 60 or 90)) throw new AgreementValidationException("FollowupInvalidRange");
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

    public async Task<AgreementFollowupDetails> GetFollowupAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, edit, manage) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        await new AgreementDeadlineInitializer(factory, Clock).EnsureAccountAsync(accountId, ct);
        await db.Entry(a).ReloadAsync(ct);
        var settings = await SettingsAsync(db, accountId, ct);
        var today = AgreementReminderSchedule.Today(Clock.GetUtcNow(), settings.TimeZoneId);
        var editors = await AgreementFollowupAccess.Editors(db, accountId, a).OrderBy(x => x.FirstName)
            .Select(x => new AgreementOptionDto(x.Id, x.FirstName + " " + x.LastName, null)).ToListAsync(ct);
        var deadlines = await db.AgreementDeadlines.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId)
            .OrderBy(x => x.State).ThenBy(x => x.DueDate).ToListAsync(ct);
        var party = await db.Organizations.Where(x => x.AccountId == accountId && x.Id == a.CounterpartyOrganizationId).Select(x => x.Name).SingleAsync(ct);
        var details = new List<AgreementDeadlineDetail>();
        foreach (var d in deadlines)
        {
            var responsibleId = d.AssignedUserId ?? a.OwnerUserId;
            var name = await db.Users.Where(x => x.Id == responsibleId).Select(x => x.FirstName + " " + x.LastName).SingleAsync(ct);
            var history = await (from h in db.AgreementDeadlineHistory.AsNoTracking()
                join actor in db.Users on h.ActorUserId equals actor.Id into actors
                from actor in actors.DefaultIfEmpty()
                join assignee in db.Users on h.AssignedUserId equals assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                where h.AccountId == accountId && h.DeadlineId == d.Id
                orderby h.CreatedUtc, h.Id
                select new AgreementHistoryDto(h.Id, h.Kind, h.CreatedUtc,
                    actor == null ? null : actor.FirstName + " " + actor.LastName,
                    assignee == null ? null : assignee.FirstName + " " + assignee.LastName, h.Comment)).ToListAsync(ct);
            var reminders = await db.AgreementReminders.AsNoTracking().Where(x => x.AccountId == accountId && x.DeadlineId == d.Id)
                .OrderBy(x => x.ScheduledUtc).Select(x => new AgreementReminderDto(x.DaysBefore, x.Status, x.RecipientAddress,
                    x.ScheduledUtc, x.Attempts, x.LastAttemptUtc, x.SentUtc, x.TransportId, x.ResultKey)).ToListAsync(ct);
            details.Add(new(new(d.Id, a.Id, a.Title, party, a.Type, d.Kind, d.DueDate, d.DueDate.DayNumber - today.DayNumber,
                responsibleId, name, editors.Any(x => x.Id == responsibleId), d.Status, d.State, d.UpdatedUtc), d.AssignedUserId, history, reminders));
        }
        return new(a.Revision, edit && !a.IsArchived, manage && !a.IsArchived, a.IsArchived, details, edit ? editors : [],
            a.ReminderMode, a.ReminderDays, settings.Enabled, settings.Days, settings.TimeZoneId, settings.SendAt,
            string.IsNullOrWhiteSpace(reminderOptions?.Value.ApplicationBaseUrl) ? "NotConfigured" : reminderOptions.Value.TransportMode);
    }

    public async Task FollowupAsync(Guid accountId, Guid deadlineId, Guid revision, AgreementFollowupAction action,
        Guid? assignedUserId, string? comment, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var d = await db.AgreementDeadlines.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == deadlineId, ct)
            ?? throw new UnauthorizedAccessException();
        var (a, _, _) = await RequireAsync(db, accountId, d.AgreementId, true, false, ct);
        CheckRevision(a, revision);
        if (d.State != AgreementDeadlineState.Current) throw new AgreementValidationException("FollowupObsolete");
        if (!Enum.IsDefined(action) || comment?.Length > 2000) throw new AgreementValidationException("FollowupInvalidComment");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if ((action is AgreementFollowupAction.Complete or AgreementFollowupAction.Comment) && comment is null)
            throw new AgreementValidationException("FollowupCommentRequired");
        var kind = AgreementHistoryKind.Commented;
        switch (action)
        {
            case AgreementFollowupAction.Start:
                if (d.Status != AgreementFollowupStatus.Untreated) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.InReview; kind = AgreementHistoryKind.Started; break;
            case AgreementFollowupAction.Assign:
                if (assignedUserId.HasValue && !await AgreementFollowupAccess.Editors(db, accountId, a).AnyAsync(x => x.Id == assignedUserId, ct))
                    throw new AgreementValidationException("FollowupInvalidOwner");
                d.AssignedUserId = assignedUserId; kind = AgreementHistoryKind.Assigned; break;
            case AgreementFollowupAction.Complete:
                if (d.Status == AgreementFollowupStatus.Completed) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.Completed; d.CompletedUtc = Clock.GetUtcNow(); d.CompletedByUserId = userContext.Current.UserId;
                kind = AgreementHistoryKind.Completed;
                await AgreementDeadlineSynchronizer.CancelAsync(db, accountId, deadlineId, "FollowupCompleted", ct);
                break;
            case AgreementFollowupAction.Reopen:
                if (d.Status != AgreementFollowupStatus.Completed) throw new AgreementValidationException("FollowupInvalidTransition");
                d.Status = AgreementFollowupStatus.InReview; d.CompletedUtc = null; d.CompletedByUserId = null;
                kind = AgreementHistoryKind.Reopened; break;
        }
        d.UpdatedUtc = Clock.GetUtcNow(); d.UpdatedByUserId = userContext.Current.UserId;
        AgreementDeadlineSynchronizer.AddHistory(db, d, kind, d.UpdatedUtc, d.UpdatedByUserId, comment,
            d.AssignedUserId ?? a.OwnerUserId);
        Touch(a, userContext.Current.UserId);
        await SaveAsync(db, ct);
    }

    public async Task ArchiveAsync(Guid accountId, Guid agreementId, Guid revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, true, ct);
        CheckRevision(a, revision);
        a.IsArchived = true;
        Touch(a, userContext.Current.UserId);
        await AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, Clock.GetUtcNow(), userContext.Current.UserId, ct);
        await SaveAsync(db, ct);
    }

    public async Task SetReminderPreferencesAsync(Guid accountId, Guid agreementId, Guid revision,
        AgreementReminderMode mode, int[] days, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(mode)) throw new AgreementValidationException("FollowupInvalidDays");
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct);
        CheckRevision(a, revision);
        a.ReminderMode = mode;
        a.ReminderDays = mode == AgreementReminderMode.Custom ? AgreementReminderSchedule.ValidateDays(days) : [];
        Touch(a, userContext.Current.UserId);
        // Worker rechecks configuration immediately before transport, including already reserved items.
        if (mode == AgreementReminderMode.Disabled)
        {
            var ids = await db.AgreementDeadlines.Where(x => x.AccountId == accountId && x.AgreementId == agreementId).Select(x => x.Id).ToListAsync(ct);
            foreach (var id in ids) await AgreementDeadlineSynchronizer.CancelAsync(db, accountId, id, "FollowupDisabled", ct);
        }
        await SaveAsync(db, ct);
    }

    private static async Task<AgreementReminderSettings> SettingsAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken ct) =>
        await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, ct)
            ?? new AgreementReminderSettings { AccountId = accountId };

    public async Task<SaveAgreementReminderSettings> GetReminderSettingsAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        var settings = await SettingsAsync(db, accountId, ct);
        return new() { Enabled = settings.Enabled, Days = settings.Days, TimeZoneId = settings.TimeZoneId, SendAt = settings.SendAt, Revision = settings.Revision };
    }

    public async Task SaveReminderSettingsAsync(Guid accountId, SaveAgreementReminderSettings request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        var days = AgreementReminderSchedule.ValidateDays(request.Days);
        var zone = AgreementTimeZones.Validate(request.TimeZoneId);
        var settings = await db.AgreementReminderSettings.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
        if ((settings?.Revision ?? Guid.Empty) != request.Revision) throw new AgreementValidationException("AgreementConcurrencyConflict");
        if (settings is null) { settings = new() { AccountId = accountId }; db.AgreementReminderSettings.Add(settings); }
        settings.Enabled = request.Enabled; settings.Days = days; settings.TimeZoneId = zone;
        settings.SendAt = request.SendAt; settings.Revision = Guid.NewGuid();
        if (!settings.Enabled)
        {
            var queued = await db.AgreementReminders.Where(x => x.AccountId == accountId &&
                (x.Status == AgreementReminderStatus.Pending || (x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null))).ToListAsync(ct);
            foreach (var row in queued) { row.Status = AgreementReminderStatus.Skipped; row.ResultKey = "FollowupDisabled"; row.NextAttemptUtc = null; }
        }
        await SaveAsync(db, ct);
    }
}
