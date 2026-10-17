using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;
public sealed class AgreementFinancialData(IDbContextFactory<TenantPlatformDbContext> factory, ILogger<AgreementFinancialData>? logger = null) : IAgreementFinancialData
{
    public void ReportFailure(Exception exception, Guid resource) => logger?.LogError(exception, "Agreement basis processing failed for {AgreementId}.", resource);
    public async Task<Guid?> ReadBasisAgreementAsync(Guid account, Guid basis, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from b in db.AgreementBasisRecords.AsNoTracking() join a in db.Agreements.AsNoTracking() on b.AgreementId equals a.Id where b.AccountId == account && a.AccountId == account && b.Id == basis select (Guid?)a.Id).SingleOrDefaultAsync(ct);
    }
    public async Task<List<AgreementBasisListRow>> ReadBasisListAsync(AgreementListScope scope, AgreementDirection direction, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = AgreementReadQueries.Visible(db, scope).Select(x => x.Id);
        var bases = db.AgreementBasisRecords.AsNoTracking().Where(x => x.AccountId == scope.AccountId && x.Direction == direction && visible.Contains(x.AgreementId)).OrderByDescending(x => x.GeneratedUtc).Take(500);
        return await (from b in bases join snap in db.AgreementBasisSnapshotRecords.AsNoTracking()
            on new { b.AccountId, b.AgreementId, BasisId = b.Id, b.Revision } equals new { snap.AccountId, snap.AgreementId, snap.BasisId, snap.Revision }
            orderby b.GeneratedUtc descending select new AgreementBasisListRow(b, snap)).ToListAsync(ct);
    }
    public async Task<List<AgreementBulkBasisOption>> ReadBulkOptionsAsync(AgreementListScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from a in AgreementReadQueries.Visible(db, scope) join p in db.Organizations.AsNoTracking() on a.CounterpartyOrganizationId equals p.Id
            where a.Direction == AgreementDirection.Income && p.AccountId == scope.AccountId orderby a.Title, a.Id select new AgreementBulkBasisOption(a.Id, a.Title, p.Id, p.Name)).ToListAsync(ct);
    }
    public async Task<AgreementPendingApprovalData> ReadPendingApprovalsAsync(AgreementListScope scope, AgreementBasisApprovalFilter filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from b in db.AgreementBasisRecords.AsNoTracking() join a in AgreementReadQueries.Visible(db, scope) on b.AgreementId equals a.Id
            join p in db.Organizations.AsNoTracking() on a.CounterpartyOrganizationId equals p.Id
            where b.AccountId == scope.AccountId && p.AccountId == scope.AccountId && b.Status == AgreementBasisStatus.Draft select new { Basis = b, Agreement = a, Party = p.Name };
        var options = await query.Select(x => new { x.Agreement.Id, x.Agreement.Title, x.Agreement.CounterpartyOrganizationId, x.Party }).Distinct().OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Select(x => new AgreementBulkBasisOption(x.Id, x.Title, x.CounterpartyOrganizationId, x.Party)).ToListAsync(ct);
        if (filter.CounterpartyId.HasValue) query = query.Where(x => x.Agreement.CounterpartyOrganizationId == filter.CounterpartyId);
        if (filter.AgreementId.HasValue) query = query.Where(x => x.Agreement.Id == filter.AgreementId);
        if (filter.Direction.HasValue) query = query.Where(x => x.Basis.Direction == filter.Direction);
        if (filter.From.HasValue) query = query.Where(x => x.Basis.InvoiceDate >= filter.From);
        if (filter.To.HasValue) query = query.Where(x => x.Basis.InvoiceDate <= filter.To);
        var candidates = await query.OrderBy(x => x.Basis.InvoiceDate).ThenBy(x => x.Basis.Id).Select(x => new AgreementBasisCandidate(x.Basis.Id, x.Basis.AgreementId, x.Basis.Direction, x.Basis.InvoiceDate, x.Agreement.Title, x.Party, x.Agreement.IsArchived, x.Agreement.CounterpartyOrganizationId)).ToListAsync(ct);
        return new(options, candidates);
    }
    public async Task<IAgreementFinancialTransaction> BeginAsync(Guid account, Guid agreement, bool write, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            if (db.Database.IsRelational())
            {
                tx = await db.Database.BeginTransactionAsync(write ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.RepeatableRead, ct);
                if (write)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {account} AND \"Id\" = {agreement} FOR UPDATE", ct);
                }
            }
            var a = await db.Agreements.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == agreement, ct) ?? throw new UnauthorizedAccessException();
            var lines = await db.AgreementLines.Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var versions = await db.AgreementLineVersions.AsNoTracking().Include(x => x.Documents).Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var prices = await db.AgreementPriceVersions.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var selections = await db.AgreementIndexSelections.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var indexIds = selections.Where(x => x.IndexId.HasValue).Select(x => x.IndexId!.Value).Distinct().ToArray();
            var indices = await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == account && indexIds.Contains(x.Id)).ToListAsync(ct);
            var values = await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == account && indexIds.Contains(x.IndexId)).ToListAsync(ct);
            var memberUsers = db.Users.AsNoTracking().Where(u => db.UserAccounts.Any(m => m.AccountId == account && m.UserId == u.Id));
            var documents = await (from d in db.AgreementDocuments.AsNoTracking()
                join user in memberUsers on d.UploadedByUserId equals user.Id into users from user in users.DefaultIfEmpty()
                where d.AccountId == account && d.AgreementId == agreement orderby d.UploadedUtc descending
                select new AgreementDocumentDto(d.Id, d.OriginalFileName, d.Size, d.Category, d.Description, d.UploadedUtc, user == null ? "" : user.FirstName + " " + user.LastName)).ToListAsync(ct);
            var actorIds = versions.Select(x => x.ActorUserId).Concat(prices.Select(x => x.ActorUserId)).Distinct().ToArray();
            var names = await memberUsers.Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, ct);
            var bases = await db.AgreementBasisRecords.Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var snapshots = await db.AgreementBasisSnapshotRecords.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var events = await db.AgreementBasisEventRecords.Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var proposals = await db.AgreementAdjustmentProposalRecords.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == agreement).ToListAsync(ct);
            var accountName = await db.Accounts.Where(x => x.Id == account).Select(x => x.Name).SingleAsync(ct);
            var party = await db.Organizations.Where(x => x.AccountId == account && x.Id == a.CounterpartyOrganizationId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "";
            return new Transaction(db, tx, write, new() { Agreement = a, AccountName = accountName, CounterpartyName = party, Bases = bases, Snapshots = snapshots, Events = events, Proposals = proposals, Lines = lines, Versions = versions, Prices = prices, Selections = selections, Indices = indices, IndexValues = values, Documents = documents, ActorNames = names });
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction? tx, bool write, AgreementFinancialState state) : IAgreementFinancialTransaction
    {
        private readonly HashSet<Guid> bases = state.Bases.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> snapshots = state.Snapshots.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> events = state.Events.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> lines = state.Lines.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> versions = state.Versions.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> prices = state.Prices.Select(x => x.Id).ToHashSet();
        public AgreementFinancialState State => state;
        public async Task CommitAsync(CancellationToken ct)
        {
            if (!write) throw new InvalidOperationException("Read-only financial snapshot.");
            db.AgreementBasisRecords.AddRange(state.Bases.Where(x => !bases.Contains(x.Id)));
            db.AgreementBasisSnapshotRecords.AddRange(state.Snapshots.Where(x => !snapshots.Contains(x.Id)));
            db.AgreementBasisEventRecords.AddRange(state.Events.Where(x => !events.Contains(x.Id)));
            db.AgreementLines.AddRange(state.Lines.Where(x => !lines.Contains(x.Id)));
            db.AgreementLineVersions.AddRange(state.Versions.Where(x => !versions.Contains(x.Id)));
            db.AgreementPriceVersions.AddRange(state.Prices.Where(x => !prices.Contains(x.Id)));
            try { await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" }) { throw new AgreementValidationException("AgreementReferenceChanged"); }
        }
        public async ValueTask DisposeAsync() { try { if (tx is not null) await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
