using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.ServiceRequests;
public sealed class InboundServiceRequestData(IDbContextFactory<TenantPlatformDbContext> factory) : IInboundServiceRequestData
{
    public async Task<InboundRequestTarget?> ResolveTokenAsync(string token, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServiceRequests.AsNoTracking().Where(x => x.ReplyToken == token).Select(x => new InboundRequestTarget(x.AccountId, x.Id)).SingleOrDefaultAsync(ct);
    }
    public async Task<IInboundServiceRequestTransaction> BeginAsync(InboundRequestTarget target, string token, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            var row = await db.ServiceRequests.FromSqlInterpolated($"SELECT * FROM service_requests WHERE \"AccountId\" = {target.AccountId} AND \"Id\" = {target.RequestId} AND \"ReplyToken\" = {token} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
            return new Transaction(db, tx, row);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, ServiceRequest request) : IInboundServiceRequestTransaction
    {
        public Task<InboundRequestTarget?> ReadMessageOwnerAsync(string externalId, CancellationToken ct) =>
            (from message in db.ServiceRequestMessages.AsNoTracking() join parent in db.ServiceRequests.AsNoTracking() on message.ServiceRequestId equals parent.Id
             where message.ExternalMessageId == externalId select new InboundRequestTarget(parent.AccountId, parent.Id)).SingleOrDefaultAsync(ct);
        public async Task<bool> InsertAsync(ServiceRequestMessage message, CancellationToken ct)
        {
            if (message.ServiceRequestId != request.Id || message.CreatedByUserId.HasValue) throw new UnauthorizedAccessException();
            db.ServiceRequestMessages.Add(message);
            try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return false; }
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
