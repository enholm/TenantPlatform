namespace TenantPlatform.Infrastructure.Agreements;

public interface IAgreementDocumentStorage
{
    long MaxFileSizeBytes { get; }
    Task<StoredAgreementFile> StoreAsync(Stream source, string originalFileName, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DiscardUncommittedAsync(string storageKey);
}

public record StoredAgreementFile(string StorageKey, string FileName, string MediaType, long Size);
public class AgreementFileException(string resourceKey) : Exception(resourceKey);

public class AgreementDocumentStorageOptions
{
    public string RootPath { get; set; } = "App_Data/agreement-documents";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}
