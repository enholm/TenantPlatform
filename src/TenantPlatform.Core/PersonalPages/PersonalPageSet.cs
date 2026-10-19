namespace TenantPlatform.Core.PersonalPages;

// Definitions only. No report results or credentials belong in this record.
public sealed class PersonalPageSet
{
    public Guid AccountId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid Revision { get; set; }
    public string DefinitionsJson { get; set; } = "[]";
}
