using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementFollowupWriteData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementFollowupWriteData
{
    public async Task<Guid?> ReadDeadlineAgreementAsync(Guid account, Guid deadline, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from d in db.AgreementDeadlines.AsNoTracking()
            join a in db.Agreements.AsNoTracking() on d.AgreementId equals a.Id
            where d.AccountId == account && a.AccountId == account && d.Id == deadline select (Guid?)a.Id).SingleOrDefaultAsync(ct);
    }
    public async Task<TenantPlatform.Core.Agreements.AgreementReminderSettings?> ReadSettingsAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementReminderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account, ct);
    }
    public async Task<IAgreementFollowupWriteTransaction> BeginAsync(Guid account, Guid? agreement, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            if (agreement.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {account} AND \"Id\" = {agreement.Value} FOR UPDATE", ct);
            var a = agreement.HasValue ? await db.Agreements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct) ?? throw new UnauthorizedAccessException() : null;
            var deadlines = a is null ? [] : await db.AgreementDeadlines.Where(x => x.AccountId == account && x.AgreementId == a.Id).ToListAsync(ct);
            var ids = deadlines.Select(x => x.Id).ToArray();
            var reminders = await db.AgreementReminders.Where(x => x.AccountId == account && (!agreement.HasValue || ids.Contains(x.DeadlineId))).ToListAsync(ct);
            var candidates = a is null ? [] : await AgreementFollowupAccess.Editors(db, account, a).Select(x => x.Id).ToListAsync(ct);
            var settings = a is null ? await db.AgreementReminderSettings.SingleOrDefaultAsync(x => x.AccountId == account, ct) : null;
            return new Transaction(db, tx, new() { Agreement = a, Deadlines = deadlines, Reminders = reminders, ResponsibleCandidates = candidates.ToHashSet(), Settings = settings });
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, AgreementFollowupWriteState state) : IAgreementFollowupWriteTransaction
    {
        private readonly bool hadSettings = state.Settings is not null;
        public AgreementFollowupWriteState State => state;
        public Task SynchronizeAsync(DateTimeOffset now, Guid user, CancellationToken ct) => AgreementDeadlineSynchronizer.SynchronizeAsync(db, state.Agreement ?? throw new InvalidOperationException(), now, user, ct);
        public async Task CommitAsync(CancellationToken ct)
        {
            if (!hadSettings && state.Settings is not null) db.AgreementReminderSettings.Add(state.Settings);
            db.AgreementDeadlineHistory.AddRange(state.History); db.AgreementNoticeHistory.AddRange(state.NoticeHistory);
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
