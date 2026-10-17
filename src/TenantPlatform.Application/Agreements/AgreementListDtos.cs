using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public enum AgreementAttention { Notice, Renewal, Expired }
public record AgreementListItemDto(Guid Id, string Title, string CounterpartyName, AgreementType Type,
    AgreementStatus Status, Guid OwnerUserId, string OwnerName, DateOnly? EndDate, DateOnly? NoticeDeadline, bool IsArchived = false);
public record AgreementOptionDto(Guid Id, string Name, Guid? BuildingId = null, bool IsActive = true);
public record AgreementPageDto(List<AgreementListItemDto> Items, int TotalCount, int Page, int PageSize, List<AgreementOptionDto> Owners);
public class AgreementFilter
{
    public AgreementAttention? Attention { get; set; }
    public string? Search { get; set; }
    public AgreementStatus? Status { get; set; }
    public AgreementType? Type { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int Page { get; set; } = 1;
}

public class AgreementValidationException(string resourceKey) : Exception(resourceKey);
