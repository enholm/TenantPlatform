using TenantPlatform.Application.Agreements;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public static class AgreementDeadlineSynchronizer
{
    public static async Task SynchronizeAsync(TenantPlatformDbContext db, Agreement agreement,
        DateTimeOffset now, Guid? actor, CancellationToken ct)
    {
        AgreementNoticeRules.Recalculate(agreement);
        var current = await db.AgreementDeadlines.Where(x => x.AccountId == agreement.AccountId &&
            x.AgreementId == agreement.Id && x.State == AgreementDeadlineState.Current).ToListAsync(ct);
        foreach (var (kind, date) in new[] { (AgreementDeadlineKind.Notice, agreement.NoticeDeadline),
            (AgreementDeadlineKind.Expiry, agreement.EndDate), (AgreementDeadlineKind.Renewal, agreement.RenewalDate),
            (AgreementDeadlineKind.Cessation, agreement.CessationDate) })
        {
            var old = current.SingleOrDefault(x => x.Kind == kind);
            var due = agreement.IsArchived ? null : date;
            if (old is not null && old.DueDate == due && old.PeriodStartDate == agreement.CurrentPeriodStartDate) continue;
            if (old is not null)
            {
                old.State = due.HasValue ? AgreementDeadlineState.Replaced : AgreementDeadlineState.Withdrawn;
                old.UpdatedUtc = now; old.UpdatedByUserId = actor;
                AddHistory(db, old, agreement.IsArchived ? AgreementHistoryKind.Archived :
                    due.HasValue ? AgreementHistoryKind.Replaced : AgreementHistoryKind.Withdrawn, now, actor);
                await CancelAsync(db, agreement.AccountId, old.Id, "FollowupObsolete", ct);
            }
            if (due.HasValue)
            {
                var deadline = new AgreementDeadline { Id = Guid.NewGuid(), AccountId = agreement.AccountId,
                    AgreementId = agreement.Id, Kind = kind, DueDate = due.Value, PeriodStartDate = agreement.CurrentPeriodStartDate,
                    CreatedUtc = now, UpdatedUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor };
                db.AgreementDeadlines.Add(deadline);
                AddHistory(db, deadline, AgreementHistoryKind.Created, now, actor, effectiveAssignee: agreement.OwnerUserId);
            }
        }
        agreement.DeadlinesInitialized = true;
    }

    public static void AddHistory(TenantPlatformDbContext db, AgreementDeadline deadline, AgreementHistoryKind kind,
        DateTimeOffset now, Guid? actor, string? comment = null, Guid? effectiveAssignee = null) =>
        db.AgreementDeadlineHistory.Add(new AgreementDeadlineHistory { Id = Guid.NewGuid(),
            AccountId = deadline.AccountId, DeadlineId = deadline.Id, Kind = kind, CreatedUtc = now,
            ActorUserId = actor, AssignedUserId = effectiveAssignee ?? deadline.AssignedUserId, Comment = comment });

    public static async Task CancelAsync(TenantPlatformDbContext db, Guid accountId, Guid deadlineId, string reason, CancellationToken ct)
    {
        // Tracked updates commit atomically with the parent agreement change.
        var queued = await db.AgreementReminders.Where(x => x.AccountId == accountId && x.DeadlineId == deadlineId &&
            (x.Status == AgreementReminderStatus.Pending || (x.Status == AgreementReminderStatus.Failed && x.NextAttemptUtc != null))).ToListAsync(ct);
        foreach (var item in queued) { item.Status = AgreementReminderStatus.Skipped; item.ResultKey = reason; item.NextAttemptUtc = null; }
    }
}

public class AgreementDeadlineInitializer(IDbContextFactory<TenantPlatformDbContext> factory, TimeProvider clock) : IAgreementInitializationData
{
    public Task EnsureAccountAsync(Guid accountId, CancellationToken ct = default) => EnsureAsync(accountId, null, ct);

    public Task EnsureVisibleAsync(AgreementListScope scope, Guid? agreementId, CancellationToken ct) => EnsureAsync(scope.AccountId, agreementId, ct, scope);

    private async Task EnsureAsync(Guid accountId, Guid? agreementId, CancellationToken ct, AgreementListScope? scope = null)
    {
        // Parent revision serializes lazy backfill against edits and other workers.
        while (true)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var visible = scope is null ? db.Agreements.Where(x => x.AccountId == accountId)
                : db.Agreements.Where(x => x.AccountId == scope.AccountId && (scope.CanReadAll || x.OwnerUserId == scope.UserId ||
                    db.AgreementAccess.Any(g => g.AccountId == scope.AccountId && g.AgreementId == x.Id && g.UserId == scope.UserId)));
            var agreements = await visible.Where(x => x.AccountId == accountId && !x.DeadlinesInitialized && (!agreementId.HasValue || x.Id == agreementId))
                .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
            if (agreements.Count == 0) return;
            foreach (var agreement in agreements)
            {
                await AgreementDeadlineSynchronizer.SynchronizeAsync(db, agreement, clock.GetUtcNow(), null, ct);
                agreement.Revision = Guid.NewGuid();
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* Reload on next bounded batch; competing edit also initializes. */ }
        }
    }
}
