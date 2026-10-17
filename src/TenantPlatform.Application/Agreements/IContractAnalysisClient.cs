using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public record ContractApiReply(string? Json, string? ResponseId, string Model, string Status,
    long? InputTokens, long? CachedInputTokens, long? OutputTokens);
public interface IContractAnalysisClient
{
    string Model { get; }
    Task<ContractApiReply> AnalyzeAsync(string ownBusiness, IReadOnlyList<AgreementAnalysisFile> files, CancellationToken ct);
}

