namespace TenantPlatform.Core.Agreements;

public enum AgreementAnalysisState { Draft = 1, Analyzing = 2, Ready = 3, Approved = 4, Discarded = 5 }
public enum AgreementFindingCategory { Counterparty = 1, Liability, LiabilityLimit, Exceptions, IndirectLoss, Indemnity, Termination, Payment, Insurance, Confidentiality, GoverningLaw }
public enum AgreementFindingStatus { Found = 1, NotFound = 2, Unclear = 3 }

// A draft becomes an immutable approved analysis version. Usage survives draft deletion.
public class AgreementAnalysis
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? AgreementId { get; set; }
    public Guid? ExpectedAgreementRevision { get; set; }
    public AgreementAnalysisState State { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? OriginalResultJson { get; set; }
    public string? Model { get; set; }
    public string SchemaVersion { get; set; } = "contract-v1";
    public string? ApprovedCounterpartyName { get; set; }
    public string? ApprovedOrganizationNumber { get; set; }
    public string? ApprovedAddress { get; set; }
    public Guid? ApprovedOrganizationId { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public List<AgreementAnalysisFile> Files { get; set; } = [];
    public List<AgreementFinding> Findings { get; set; } = [];
}

public class AgreementAnalysisFile
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AnalysisId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long Size { get; set; }
    public AgreementDocumentCategory Category { get; set; }
    public Guid? AgreementDocumentId { get; set; }
    public bool PendingDelete { get; set; }
}

public class AgreementFinding
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid AnalysisId { get; set; }
    public int Position { get; set; }
    public AgreementFindingCategory Category { get; set; }
    public AgreementFindingStatus Status { get; set; }
    public AgreementFindingStatus OriginalStatus { get; set; }
    public string OriginalValue { get; set; } = "";
    public string? AdjustedValue { get; set; }
    public string Parties { get; set; } = "";
    public string OriginalParties { get; set; } = "";
    public string Explanation { get; set; } = "";
    public List<AgreementFindingSource> Sources { get; set; } = [];
}

public class AgreementFindingSource
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid FindingId { get; set; }
    public Guid DocumentId { get; set; }
    public string Quote { get; set; } = "";
    public string Section { get; set; } = "";
    public int? Page { get; set; }
}

// Deliberately no FK to the transient analysis: contains no document or result content.
public class AgreementAiUsage
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AnalysisId { get; set; }
    public Guid UserId { get; set; }
    public string? ResponseId { get; set; }
    public string Model { get; set; } = "";
    public long? InputTokens { get; set; }
    public long? CachedInputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string Status { get; set; } = "Started";
}
