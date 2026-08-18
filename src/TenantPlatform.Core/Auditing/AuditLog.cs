namespace TenantPlatform.Core.Auditing;

public class AuditLog
{
    public Guid Id { get; set; }

    public Guid? AccountId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserEmail { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? ChangesJson { get; set; }
}

