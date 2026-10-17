using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public class AgreementOptionsDto
{
    public List<AgreementOptionDto> OrganizationElements { get; init; } = [];
    public List<AgreementOptionDto> GeographicAreas { get; init; } = [];
    public List<AgreementOptionDto> Indices { get; init; } = [];
    public List<AgreementOptionDto> Counterparties { get; init; } = [];
    public List<AgreementOptionDto> Members { get; init; } = [];
    public List<AgreementOptionDto> Buildings { get; init; } = [];
    public List<AgreementOptionDto> Units { get; init; } = [];
}
public record AgreementDownload(Stream Content, string FileName, string MediaType);
