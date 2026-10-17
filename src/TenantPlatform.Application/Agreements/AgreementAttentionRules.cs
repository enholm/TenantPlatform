using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public enum AgreementAttentionDate { Notice, Renewal, End }
public sealed record AgreementAttentionCriteria(AgreementAttentionDate Date,
    IReadOnlyList<AgreementStatus> Statuses, DateOnly? FromInclusive, DateOnly? ThroughInclusive, DateOnly? Before, bool ExcludeArchived = true);

public static class AgreementAttentionRules
{
    public const int HorizonDays = 90;

    public static AgreementAttentionCriteria Create(AgreementAttention kind, DateOnly today)
    {
        var until = today.AddDays(HorizonDays);
        return kind switch
        {
            AgreementAttention.Notice => new(AgreementAttentionDate.Notice, [AgreementStatus.Active], today, until, null),
            AgreementAttention.Renewal => new(AgreementAttentionDate.Renewal, [AgreementStatus.Active], today, until, null),
            AgreementAttention.Expired => new(AgreementAttentionDate.End, [AgreementStatus.Active, AgreementStatus.Expired], null, null, today),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
