namespace TenantPlatform.Core.Agreements;

public class AgreementDocument
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long Size { get; set; }
    public AgreementDocumentCategory Category { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset UploadedUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
}
