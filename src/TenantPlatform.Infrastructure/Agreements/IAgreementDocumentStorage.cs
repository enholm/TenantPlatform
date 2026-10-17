namespace TenantPlatform.Infrastructure.Agreements;

public class AgreementDocumentStorageOptions
{
    public string RootPath { get; set; } = "App_Data/agreement-documents";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}
