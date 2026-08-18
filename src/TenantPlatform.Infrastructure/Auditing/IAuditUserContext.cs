namespace TenantPlatform.Infrastructure.Auditing;

public interface IAuditUserContext
{
    Guid? UserId { get; }

    Guid? AccountId { get; }

    string? Email { get; }
}
