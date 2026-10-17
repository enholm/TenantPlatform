namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task UploadOrderAsync(Guid account, Guid id, Guid revision, string fileName, Stream content, CancellationToken ct = default) => documents.UploadOrderAsync(account, id, revision, fileName, content, ct);
}
