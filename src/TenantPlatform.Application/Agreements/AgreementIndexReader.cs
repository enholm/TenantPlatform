using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public enum IndexValueView { Current, History }
public sealed record IndexEntry(Guid Id, string Code, string Name, string Description, string? SourceOrigin, AgreementIndexResolution Resolution);
public sealed record IndexValueEntry(Guid Id, DateOnly Period, Guid PeriodKey, int Revision, bool Superseded, decimal Value, DateOnly? PublishedDate);
public interface IAgreementIndexReadData
{
    Task<FinancePage<IndexEntry>> RegisterAsync(AgreementListScope scope, string? search, AgreementIndexResolution? resolution, int page, CancellationToken ct);
    Task<FinancePage<IndexValueEntry>> ValuesAsync(AgreementListScope scope, Guid index, IndexValueView view, DateOnly? from, DateOnly? to, int page, CancellationToken ct);
}
public sealed class AgreementIndexReader(IAuthorizationService authorization, IAgreementIndexReadData data)
{
    // Source is arbitrary legacy text, not a trusted public URL. Publish only the origin of
    // an absolute HTTP(S) URL without userinfo. Paths, queries, fragments and text are withheld.
    public static string? SourceOrigin(string? source) => !string.IsNullOrWhiteSpace(source) && !source.Any(char.IsControl) &&
        Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.HostNameType == UriHostNameType.Dns
        ? uri.GetLeftPart(UriPartial.Authority) : null;
    private static void Page(int page) { if (page is < 1 or > 10000) throw new ArgumentException(); }
    private async Task Revalidate(AgreementListScope scope, CancellationToken ct)
    {
        if ((await authorization.GetAgreementListScopeAsync(scope.AccountId, ct)).UserId != scope.UserId) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
    }
    public async Task<FinancePage<IndexEntry>> RegisterAsync(Guid account, string? search = null, AgreementIndexResolution? resolution = null, int page = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Page(page);
        if (resolution.HasValue && !Enum.IsDefined(resolution.Value) || search is not null && (search.Length > 200 || search.Any(char.IsControl))) throw new ArgumentException();
        var scope = await authorization.GetAgreementListScopeAsync(account, ct);
        var result = await data.RegisterAsync(scope, search?.Trim(), resolution, page, ct);
        await Revalidate(scope, ct);
        return result with { Items = result.Items.Select(x => x with { SourceOrigin = SourceOrigin(x.SourceOrigin) }).ToArray() };
    }
    public async Task<FinancePage<IndexValueEntry>> ValuesAsync(Guid account, Guid index, IndexValueView view = IndexValueView.Current, DateOnly? from = null, DateOnly? to = null, int page = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Page(page);
        if (index == Guid.Empty || !Enum.IsDefined(view) || from == default(DateOnly) || to == default(DateOnly) || from > to) throw new ArgumentException();
        var scope = await authorization.GetAgreementListScopeAsync(account, ct);
        var result = await data.ValuesAsync(scope, index, view, from, to, page, ct);
        await Revalidate(scope, ct);
        return result;
    }
}
