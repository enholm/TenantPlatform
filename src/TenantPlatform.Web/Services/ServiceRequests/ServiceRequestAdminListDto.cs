namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestAdminListDto
{
    public List<ServiceRequestAdminListItemDto> Requests { get; init; } = [];

    public List<ServiceRequestAdminFilterOptionDto> Services { get; init; } = [];

    public List<ServiceRequestAdminFilterOptionDto> Buildings { get; init; } = [];

    public List<ServiceRequestAdminFilterOptionDto> Providers { get; init; } = [];
}

public record ServiceRequestAdminFilterOptionDto(
    Guid Id,
    string Name);
