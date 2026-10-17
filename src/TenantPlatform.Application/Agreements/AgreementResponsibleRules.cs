using System.Linq.Expressions;
namespace TenantPlatform.Application.Agreements;
// The documented legacy assignment rule, shared by selectors, writes and delivery.
public sealed record AgreementResponsibleFacts
{
    public Guid UserId { get; init; }
    public Guid AgreementId { get; init; }
    public bool IsActive { get; init; }
    public bool IsMember { get; init; }
    public bool IsOwner { get; init; }
    public bool IsAccountAdmin { get; init; }
    public bool HasEditGrant { get; init; }
}
public static class AgreementResponsibleRules
{
    public static Expression<Func<AgreementResponsibleFacts, bool>> Eligible { get; } = x => x.IsActive && x.IsMember && (x.IsOwner || x.IsAccountAdmin || x.HasEditGrant);
    private static readonly Func<AgreementResponsibleFacts, bool> Check = Eligible.Compile();
    public static bool IsEligible(AgreementResponsibleFacts facts) => Check(facts);
}
