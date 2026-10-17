namespace TenantPlatform.Infrastructure.Agreements;
public class AgreementAnalysisOptions
{
    public string ApiKey { get; set; } = "";
    // Explicitly configured: the deployment chooses a vision model supporting Structured Outputs.
    public string Model { get; set; } = "";
}
