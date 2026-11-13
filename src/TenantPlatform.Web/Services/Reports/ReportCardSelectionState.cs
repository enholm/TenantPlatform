namespace TenantPlatform.Web.Services.Reports;

public sealed class ReportCardSelectionState
{
    public Guid? PageId { get; private set; }
    public Guid? CardId { get; private set; }
    public bool HasSelection => PageId.HasValue && CardId.HasValue;

    public void Select(Guid pageId, Guid cardId)
    {
        PageId = pageId;
        CardId = cardId;
    }

    public void Clear()
    {
        PageId = null;
        CardId = null;
    }

    public bool Matches(Guid pageId, Guid cardId) => PageId == pageId && CardId == cardId;

    public ReportCardDraft? Resolve(IEnumerable<ReportPageDraft> pages) =>
        PageId is Guid pageId && CardId is Guid cardId
            ? pages.FirstOrDefault(x => x.Id == pageId)?.Cards.FirstOrDefault(x => x.ClientId == cardId)
            : null;
}
