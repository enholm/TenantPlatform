using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementBasisReadData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementBasisReadData
{
    public async Task<Guid?> FindAgreementAsync(Guid account, Guid basis, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementBasisRecords.AsNoTracking().Where(b => b.AccountId == account && b.Id == basis)
            .Select(b => (Guid?)b.AgreementId).SingleOrDefaultAsync(ct);
    }
    public async Task<Guid[]> VisibleAsync(AgreementListScope scope, Guid[] agreements, CancellationToken ct)
    {
        // This fresh context intentionally does not reuse the data transaction's old snapshot.
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.Visible(db, scope).Where(a => agreements.Contains(a.Id)).Select(a => a.Id).ToArrayAsync(ct);
    }
    public async Task<IAgreementBasisReadSession> BeginAsync(AgreementListScope scope, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            return new Session(db, tx, scope);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed record SnapshotContent(Guid AccountId, Guid AgreementId, Guid BasisId, int Revision, string? DataJson);
    private sealed class Session(TenantPlatformDbContext db, IDbContextTransaction tx, AgreementListScope scope) : IAgreementBasisReadSession
    {
        private IQueryable<AgreementBasis> Bases => db.AgreementBasisRecords.AsNoTracking().Where(b => b.AccountId == scope.AccountId &&
            AgreementReadQueries.Visible(db, scope).Any(a => a.Id == b.AgreementId));
        // DataJson is jsonb, not text. Cast explicitly and bound it in SQL before
        // materialization; the unmapped projection does not select entity/private columns.
        private IQueryable<SnapshotContent> Snapshots => db.Database.SqlQuery<SnapshotContent>($"""
            SELECT "AccountId", "AgreementId", "BasisId", "Revision",
              CASE WHEN char_length("DataJson"::text) <= {AgreementBasisReader.MaxSnapshotCharacters}
                   THEN "DataJson"::text ELSE NULL END AS "DataJson"
            FROM public.agreement_basis_snapshots WHERE "AccountId" = {scope.AccountId}
            """);
        private IQueryable<BasisStorageRow> Rows(IQueryable<AgreementBasis> bases, int? revision = null) =>
            from b in bases
            join s in Snapshots on new { b.AccountId, b.AgreementId, BasisId = b.Id, Revision = revision ?? b.Revision }
                equals new { s.AccountId, s.AgreementId, s.BasisId, s.Revision } into snapshots
            from s in snapshots.DefaultIfEmpty()
            orderby b.GeneratedUtc descending, b.Id
            select new BasisStorageRow(b.Id, b.AgreementId, b.OriginalBasisId, b.Direction, b.InvoiceDate, b.Status, b.Revision, b.GeneratedUtc,
                (int?)s.Revision, s.DataJson);
        public async Task<FinancePage<BasisStorageRow>> ListAsync(BasisReadFilter filter, int page, CancellationToken ct)
        {
            var query = Bases.Where(b => b.Direction == filter.Direction);
            if (filter.AgreementId.HasValue) query = query.Where(b => b.AgreementId == filter.AgreementId);
            if (filter.Status.HasValue) query = query.Where(b => b.Status == filter.Status);
            if (filter.From.HasValue) query = query.Where(b => b.InvoiceDate >= filter.From);
            if (filter.To.HasValue) query = query.Where(b => b.InvoiceDate <= filter.To);
            var total = await query.CountAsync(ct); var actual = Math.Min(page, Math.Max(1, (int)(((long)total + 24) / 25)));
            var selected = query.OrderByDescending(b => b.GeneratedUtc).ThenBy(b => b.Id).Skip((actual - 1) * 25).Take(25);
            var rows = await Rows(selected).ToArrayAsync(ct);
            return new(page, actual, total, rows);
        }
        public async Task<BasisStorageRow> DetailAsync(Guid basis, int? revision, CancellationToken ct)
        {
            var row = await Rows(Bases.Where(b => b.Id == basis), revision).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
            if (revision.HasValue && row.SnapshotRevision is null) throw new UnauthorizedAccessException();
            return row;
        }
        public async Task<BasisReferenceFacts> ReferencesAsync(BasisReferenceRequest r, CancellationToken ct)
        {
            var account = scope.AccountId;
            var parties = await db.Organizations.AsNoTracking().Where(o => o.AccountId == account && r.Parties.Contains(o.Id)).Select(o => o.Id).ToArrayAsync(ct);
            var lines = await db.AgreementLines.AsNoTracking().Where(l => l.AccountId == account && r.Lines.Contains(l.Id)).Select(l => new BasisResourceReference(l.Id, l.AgreementId, null)).ToArrayAsync(ct);
            var versions = await db.AgreementLineVersions.AsNoTracking().Where(v => v.AccountId == account && r.Versions.Contains(v.Id)).Select(v => new BasisResourceReference(v.Id, v.AgreementId, v.LineId)).ToArrayAsync(ct);
            var prices = await db.AgreementPriceVersions.AsNoTracking().Where(p => p.AccountId == account && r.Prices.Contains(p.Id)).Select(p => new BasisResourceReference(p.Id, p.AgreementId, p.LineId)).ToArrayAsync(ct);
            var bases = await db.AgreementBasisRecords.AsNoTracking().Where(b => b.AccountId == account && r.Bases.Contains(b.Id)).Select(b => new BasisResourceReference(b.Id, b.AgreementId, null)).ToArrayAsync(ct);
            var snapshots = await db.AgreementBasisSnapshotRecords.AsNoTracking().Where(s => s.AccountId == account && r.Snapshots.Contains(s.Id)).Select(s => new BasisResourceReference(s.Id, s.AgreementId, null)).ToArrayAsync(ct);
            return new(parties, lines, versions, prices, bases, snapshots);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
