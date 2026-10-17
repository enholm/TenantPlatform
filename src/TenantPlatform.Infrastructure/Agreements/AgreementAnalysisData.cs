using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementAnalysisData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementAnalysisData
{
    public async Task<IAgreementAnalysisTransaction> BeginAsync(Guid account, Guid? draft, Guid actor, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            var row = draft.HasValue ? await db.AgreementAnalyses.FromSqlInterpolated($"SELECT * FROM agreement_analyses WHERE \"AccountId\" = {account} AND \"Id\" = {draft.Value} AND \"CreatedByUserId\" = {actor} FOR UPDATE").SingleOrDefaultAsync(ct) : null;
            if (row is not null) row.Files = await db.AgreementAnalysisFiles.Where(x => x.AccountId == account && x.AnalysisId == row.Id).ToListAsync(ct);
            return new Transaction(db, tx, account, actor, row);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public async Task<Guid> ReadAgreementRevisionAsync(Guid account, Guid agreement, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Agreements.Where(x => x.AccountId == account && x.Id == agreement).Select(x => x.Revision).SingleAsync(ct); }
    public async Task<List<AgreementAnalysis>> ReadApprovedAsync(Guid account, Guid agreement, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementAnalyses.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreement && x.State == AgreementAnalysisState.Approved)
            .Include(x => x.Findings.Where(f => f.AccountId == account && f.AgreementId == agreement)).ThenInclude(x => x.Sources.Where(s => s.AccountId == account && s.AgreementId == agreement))
            .Include(x => x.Files.Where(f => f.AccountId == account)).AsSplitQuery().OrderByDescending(x => x.ApprovedUtc).ToListAsync(ct);
    }
    public async Task RecordUsageAsync(Guid account, Guid draft, Guid attempt, ContractApiReply reply, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var usage = await db.AgreementAiUsage.SingleAsync(x => x.AccountId == account && x.AnalysisId == draft && x.Id == attempt, ct);
        usage.ResponseId = reply.ResponseId; usage.Model = reply.Model; usage.Status = reply.Status;
        usage.InputTokens = reply.InputTokens; usage.CachedInputTokens = reply.CachedInputTokens; usage.OutputTokens = reply.OutputTokens;
        await db.SaveChangesAsync(ct);
    }
    public async Task ResetAttemptAsync(Guid account, Guid draft, Guid attempt, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.AgreementAnalyses.Where(x => x.AccountId == account && x.Id == draft && x.AttemptId == attempt && x.State == AgreementAnalysisState.Analyzing)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AgreementAnalysisState.Draft).SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), ct);
        await db.AgreementAiUsage.Where(x => x.AccountId == account && x.AnalysisId == draft && x.Id == attempt && x.Status == "Started")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "FailedOrUnknown"), ct);
    }
    public async Task<bool> HasFileMetadataAsync(Guid account, string key, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.AgreementAnalysisFiles.AnyAsync(x => x.AccountId == account && x.StorageKey == key, ct); }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, Guid actor, AgreementAnalysis? original) : IAgreementAnalysisTransaction
    {
        private readonly bool creating = original is null;
        private readonly HashSet<Guid> files = original?.Files.Select(x => x.Id).ToHashSet() ?? [];
        private AgreementManagementState? approval;
        public AgreementAnalysis? Draft { get; set; } = original;
        public List<AgreementDocument> Documents { get; } = [];
        public AgreementAiUsage? Usage { get; set; }
        public Task<string> ReadAccountNameAsync(CancellationToken ct) => db.Accounts.Where(x => x.Id == account).Select(x => x.Name).SingleAsync(ct);
        public async Task<List<Organization>> ReadOrganizationsAsync(bool lockAccount, CancellationToken ct)
        {
            if (lockAccount) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            return await db.Organizations.Where(x => x.AccountId == account).ToListAsync(ct);
        }
        public async Task PersistOrganizationAsync(Organization organization, CancellationToken ct)
        {
            if (organization.AccountId != account) throw new UnauthorizedAccessException();
            db.Organizations.Add(organization); await db.SaveChangesAsync(ct);
        }
        public async Task<AgreementManagementState> PrepareApprovalAsync(Guid? agreement, CancellationToken ct)
        {
            if (agreement.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {account} AND \"Id\" = {agreement.Value} FOR UPDATE", ct);
            var a = agreement.HasValue ? await db.Agreements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct) ?? throw new UnauthorizedAccessException() : null;
            approval = new() { Agreement = a, References = await AgreementManagementData.References(db, account, ct), HasActivatedLines = a is not null && await db.AgreementLines.AnyAsync(x => x.AccountId == account && x.AgreementId == a.Id && x.ActivatedUtc != null, ct) };
            return approval;
        }
        public Task SynchronizeApprovalAsync(DateTimeOffset now, Guid user, CancellationToken ct)
        {
            var a = approval?.Agreement ?? throw new InvalidOperationException();
            if (db.Entry(a).State == EntityState.Detached) db.Agreements.Add(a);
            return AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, now, user, ct);
        }
        public async Task CommitAsync(CancellationToken ct)
        {
            var draft = Draft ?? throw new InvalidOperationException();
            if (draft.AccountId != account || draft.CreatedByUserId != actor || draft.Files.Any(x => x.AccountId != account || x.AnalysisId != draft.Id)) throw new UnauthorizedAccessException();
            if (creating) db.AgreementAnalyses.Add(draft);
            else db.AgreementAnalysisFiles.AddRange(draft.Files.Where(x => !files.Contains(x.Id)));
            if (Usage is not null) { if (Usage.AccountId != account || Usage.AnalysisId != draft.Id || Usage.UserId != actor) throw new UnauthorizedAccessException(); db.AgreementAiUsage.Add(Usage); }
            if (approval is not null)
            {
                var a = approval.Agreement ?? throw new InvalidOperationException(); if (a.AccountId != account) throw new UnauthorizedAccessException();
                if (db.Entry(a).State == EntityState.Detached) db.Agreements.Add(a);
                db.AgreementNoticeHistory.AddRange(approval.NoticeHistory); db.AgreementIndexSelections.AddRange(approval.Selections);
            }
            if (Documents.Any(x => x.AccountId != account || x.AgreementId != draft.AgreementId)) throw new UnauthorizedAccessException();
            db.AgreementDocuments.AddRange(Documents); db.AgreementFindings.AddRange(draft.Findings);
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public Task RollbackAsync(CancellationToken ct) => tx.RollbackAsync(ct);
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
