using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing.Invoices;
public sealed record InvoiceWorkTarget(Guid AccountId, Guid Id);
public interface ILeasingInvoiceJobData
{
    Task<InvoiceWorkTarget?> ReadCandidateAsync(DateTimeOffset expired, CancellationToken ct);
    Task<bool> ClaimAsync(InvoiceWorkTarget target, Guid token, DateTimeOffset now, DateTimeOffset expired, CancellationToken ct);
    Task<LeasingInvoice?> ReadClaimedAsync(InvoiceWorkTarget target, Guid token, CancellationToken ct);
    Task<ILeasingInvoiceJobTransaction> BeginPublishAsync(InvoiceWorkTarget target, Guid token, CancellationToken ct);
}
public interface ILeasingInvoiceJobTransaction : IAsyncDisposable
{
    LeasingInvoice? Invoice { get; }
    Task CommitAsync(LeasingInvoiceInterpretation? interpretation, LeasingInvoiceHistory history, CancellationToken ct);
}
public sealed class LeasingInvoiceJob(ILeasingInvoiceJobData data, IInvoiceDocumentInterpreter interpreter, TimeProvider clock, ISystemWorkContext system)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.LeasingInvoices);
        var now = clock.GetUtcNow(); var expired = now.AddMinutes(-10);
        var candidate = await data.ReadCandidateAsync(expired, ct); if (candidate is null) return false;
        var token = Guid.NewGuid(); if (!await data.ClaimAsync(candidate, token, now, expired, ct)) return true;
        var invoice = await data.ReadClaimedAsync(candidate, token, ct); if (invoice is null) return true;
        InvoiceInterpretationResult? result = null; string? error = null;
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5)); result = await interpreter.InterpretAsync(invoice, timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (LeasingValidationException ex) { error = ex.Message; }
        catch (Exception) { error = "InvoiceProcessingFailed"; }
        await using var tx = await data.BeginPublishAsync(candidate, token, ct);
        var entity = tx.Invoice; if (entity is null) return true;
        entity.Processing = result is null ? LeasingInvoiceProcessing.Failed : LeasingInvoiceProcessing.Ready; entity.ProcessingError = error;
        entity.ProcessingToken = null; entity.Revision = Guid.NewGuid(); LeasingInvoiceInterpretation? interpretation = null;
        if (result is not null)
        {
            interpretation = new() { Id = Guid.NewGuid(), AccountId = entity.AccountId, InvoiceId = entity.Id, CreatedUtc = clock.GetUtcNow(), Version = result.Version,
                ResultJson = JsonSerializer.Serialize(result.Data), ExtractedText = result.Text, WarningsJson = JsonSerializer.Serialize(result.Warnings) };
            if (entity.ReviewedUtc is null && entity.ReviewJson == "{}") entity.ReviewJson = entity.Category == LeasingInvoiceCategory.Rental ? JsonSerializer.Serialize(new RentalInvoiceReview { Data = result.Data }) : JsonSerializer.Serialize(new InvoiceReview { Data = result.Data, AcquisitionId = entity.AcquisitionId });
        }
        await tx.CommitAsync(interpretation, new() { Id = Guid.NewGuid(), AccountId = entity.AccountId, InvoiceId = entity.Id, RecordedUtc = clock.GetUtcNow(), Action = result is null ? "ProcessingFailed" : "Interpreted", Reason = error ?? result!.Version }, ct);
        return true;
    }
}
