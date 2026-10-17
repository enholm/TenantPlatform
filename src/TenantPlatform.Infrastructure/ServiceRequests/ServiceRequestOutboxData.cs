using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.ServiceRequests;
public sealed class ServiceRequestOutboxData(IDbContextFactory<TenantPlatformDbContext> factory, ILogger<ServiceRequestOutboxData>? logger = null) : IServiceRequestOutboxData
{
    public async Task<IServiceRequestOutboxTransaction> BeginNextAsync(DateTimeOffset now, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            // No schema/lease is added. A row lock prevents simultaneous live workers sending one item.
            var message = await db.EmailOutboxMessages.FromSqlInterpolated($"SELECT * FROM email_outbox_messages WHERE \"Status\" = {(int)EmailOutboxStatus.Pending} AND (\"NextAttemptAt\" IS NULL OR \"NextAttemptAt\" <= {now}) ORDER BY \"CreatedAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
            ServiceRequest? request = null; var active = false; List<ServiceDefinitionProvider> providers = []; List<ServiceRequestMessage> inbound = [];
            if (message is not null)
            {
                active = await db.Accounts.AnyAsync(x => x.Id == message.AccountId && x.IsActive, ct);
                request = await db.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == message.AccountId && x.Id == message.ServiceRequestId, ct);
                if (request is not null)
                {
                    providers = await (from provider in db.ServiceDefinitionProviders.AsNoTracking() join org in db.Organizations.AsNoTracking() on provider.ServiceProviderOrganizationId equals org.Id
                        join definition in db.ServiceDefinitions.AsNoTracking() on provider.ServiceDefinitionId equals definition.Id
                        where provider.AccountId == message.AccountId && org.AccountId == message.AccountId && definition.AccountId == message.AccountId && provider.ServiceDefinitionId == request.ServiceDefinitionId select provider).ToListAsync(ct);
                    inbound = await db.ServiceRequestMessages.AsNoTracking().Where(x => x.ServiceRequestId == request.Id && x.Direction == ServiceRequestMessageDirection.Inbound && x.Type == ServiceRequestMessageType.Email).ToListAsync(ct);
                }
            }
            return new Transaction(db, tx, new(message, active, request, providers, inbound));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    public void ReportFailure(Guid id, int attempt, string errorType) => logger?.LogWarning("Outbox {MessageId}, attempt {AttemptCount}, failed with {ErrorType}.", id, attempt, errorType);
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, ServiceRequestOutboxWork state) : IServiceRequestOutboxTransaction
    {
        public ServiceRequestOutboxWork State => state;
        public async Task CommitAsync(ServiceRequestMessage? history, CancellationToken ct)
        {
            if (history is not null) { if (state.Request is null || history.ServiceRequestId != state.Request.Id || state.Request.AccountId != state.Message?.AccountId) throw new UnauthorizedAccessException(); db.ServiceRequestMessages.Add(history); }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
