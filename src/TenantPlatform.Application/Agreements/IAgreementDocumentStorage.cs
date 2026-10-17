namespace TenantPlatform.Application.Agreements;

public interface IAgreementDocumentStorage
{
    long MaxFileSizeBytes { get; }
    Task<StoredAgreementFile> StoreAsync(Stream source, string originalFileName, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DiscardUncommittedAsync(string storageKey);
    Task DeleteAsync(string storageKey) => DiscardUncommittedAsync(storageKey);
}

public record StoredAgreementFile(string StorageKey, string FileName, string MediaType, long Size);
public class AgreementFileException(string resourceKey) : Exception(resourceKey);

