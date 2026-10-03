using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Leasing.Invoices;
public sealed class LeasingInvoiceProcessor(IDbContextFactory<TenantPlatformDbContext> factory, IInvoiceDocumentInterpreter interpreter, TimeProvider clock)
{
    // Persisted queue with leases: a process crash is recoverable and two workers cannot publish one attempt twice.
    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow(); var expired = now.AddMinutes(-10);
        var candidate = await db.LeasingInvoices.AsNoTracking().Where(x => x.Status == LeasingInvoiceStatus.Review &&
            (x.Processing == LeasingInvoiceProcessing.Uploaded || x.Processing == LeasingInvoiceProcessing.Processing && x.ProcessingStartedUtc < expired))
            .OrderBy(x => x.UploadedUtc).Select(x => new { x.Id, x.AccountId }).FirstOrDefaultAsync(ct);
        if (candidate == null) return false;
        var token = Guid.NewGuid();
        var claimed = await db.LeasingInvoices.Where(x => x.AccountId == candidate.AccountId && x.Id == candidate.Id && x.Status == LeasingInvoiceStatus.Review &&
            (x.Processing == LeasingInvoiceProcessing.Uploaded || x.Processing == LeasingInvoiceProcessing.Processing && x.ProcessingStartedUtc < expired))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Processing, LeasingInvoiceProcessing.Processing).SetProperty(x => x.ProcessingToken, token)
                .SetProperty(x => x.ProcessingStartedUtc, now).SetProperty(x => x.Revision, Guid.NewGuid()), ct);
        if (claimed == 0) return true;
        var invoice = await db.LeasingInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == candidate.AccountId && x.Id == candidate.Id, ct);
        if (invoice == null) return true; // The draft may have been deleted after claiming it.
        InvoiceInterpretationResult? result = null; string? error = null;
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5)); result = await interpreter.InterpretAsync(invoice, timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (LeasingValidationException ex) { error = ex.Message; }
        catch (Exception) { error = "InvoiceProcessingFailed"; } // No invoice content/provider body in logs.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {candidate.AccountId} FOR UPDATE", ct);
        var entity = await db.LeasingInvoices.SingleOrDefaultAsync(x => x.AccountId == candidate.AccountId && x.Id == candidate.Id, ct);
        if (entity == null || entity.ProcessingToken != token) return true;
        entity.Processing = result == null ? LeasingInvoiceProcessing.Failed : LeasingInvoiceProcessing.Ready; entity.ProcessingError = error;
        entity.ProcessingToken = null; entity.Revision = Guid.NewGuid();
        if (result != null)
        {
            db.LeasingInvoiceInterpretations.Add(new() { Id = Guid.NewGuid(), AccountId = entity.AccountId, InvoiceId = entity.Id, CreatedUtc = clock.GetUtcNow(),
                Version = result.Version, ResultJson = JsonSerializer.Serialize(result.Data), ExtractedText = result.Text, WarningsJson = JsonSerializer.Serialize(result.Warnings) });
            if (entity.ReviewedUtc == null && entity.ReviewJson == "{}") entity.ReviewJson = entity.Category == LeasingInvoiceCategory.Rental ? JsonSerializer.Serialize(new RentalInvoiceReview { Data = result.Data }) : JsonSerializer.Serialize(new InvoiceReview { Data = result.Data, AcquisitionId = entity.AcquisitionId });
        }
        db.LeasingInvoiceHistory.Add(new() { Id = Guid.NewGuid(), AccountId = entity.AccountId, InvoiceId = entity.Id, RecordedUtc = clock.GetUtcNow(),
            Action = result == null ? "ProcessingFailed" : "Interpreted", Reason = error ?? result!.Version });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
}
public sealed class LeasingInvoiceWorker(IServiceScopeFactory scopes, ILogger<LeasingInvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<LeasingInvoiceProcessor>().ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Invoice worker failed ({ErrorType}); retrying", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
