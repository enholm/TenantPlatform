using System.Text.Json;
using ModelContextProtocol.Protocol;
using TenantPlatform.Application.Agreements;

namespace TenantPlatform.Mcp;

public sealed record FollowupDeadlineEntry(Guid Id, string Kind, DateOnly DueDate, int DaysLeft,
    string Status, string State, DateTimeOffset UpdatedUtc);
public sealed record FollowupResult(string SchemaVersion, Guid AccountId, Guid AgreementId, bool DeadlinesInitialized,
    bool IsArchived, int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount,
    bool HasMore, int? NextPage, bool Complete, bool SnapshotConsistent, bool LimitReached, FollowupDeadlineEntry[] Items);

public sealed partial class ReadTools
{
    private static Tool FollowupTool() => Tool<FollowupResult>("agreements_followup",
        "Read stored deadline occurrences for one authorized agreement, including Current, Replaced, Withdrawn and Completed. " +
        "No deadline or settings initialization. An uninitialized agreement can legitimately have no rows. " +
        "DaysLeft is calendar days in the account reminder time zone, not 24-hour periods. " +
        "Ordered State, DueDate, ID; fixed pages of 25, no snapshot across calls. " +
        "No person identities, comments, delivery metadata, preferences or action permissions. Read only.",
        """{"type":"object","additionalProperties":false,"required":["accountId","agreementId"],"properties":{"accountId":{"type":"string","format":"uuid"},"agreementId":{"type":"string","format":"uuid"},"page":{"type":"integer","minimum":1,"maximum":10000}}}""");

    private static async Task<FollowupResult> FollowupAsync(IServiceProvider services, Guid account,
        IDictionary<string, JsonElement> args, CancellationToken ct)
    {
        var id = Reference(args, "agreementId") ?? throw new ArgumentException();
        var requested = Page(args);
        var result = await services.GetRequiredService<AgreementFollowupPageReader>().ReadAsync(account, id, requested, ct);
        ct.ThrowIfCancellationRequested();
        const int size = AgreementFollowupPageReader.PageSize;
        var total = result.Total;
        var page = result.Page;
        var rows = result.Items.Select(d => new FollowupDeadlineEntry(d.Id, d.Kind.ToString(), d.DueDate, d.DaysLeft,
            d.Status.ToString(), d.State.ToString(), d.UpdatedUtc)).ToArray();
        var more = (long)page * size < total;
        return new("1", account, id, result.Initialized, result.Archived, requested, page, size,
            total, rows.Length, more, more && page < 10000 ? page + 1 : null,
            rows.Length == total, false, more && page >= 10000, rows);
    }
}
