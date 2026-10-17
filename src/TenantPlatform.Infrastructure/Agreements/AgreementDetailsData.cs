using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementDetailsData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementDetailsData
{
    public async Task<AgreementDetailsDto> ReadAsync(AgreementReadScope scope, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var accountId = scope.ListScope.AccountId;
        var agreementId = scope.AgreementId;
        var a = await AgreementReadQueries.Visible(db, scope.ListScope).SingleOrDefaultAsync(x => x.Id == agreementId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        // Names are metadata too: users without membership in this account must not leak labels.
        var names = await db.Users.AsNoTracking().Where(u => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id) &&
            (u.Id == a.OwnerUserId || u.Id == a.CreatedByUserId || u.Id == a.UpdatedByUserId || u.Id == a.TerminationRegisteredByUserId))
            .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        var elements = await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == accountId)
            .Select(x => new { x.Id, x.ParentId, x.Name }).ToListAsync(cancellationToken);
        var areas = await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == accountId)
            .Select(x => new { x.Id, x.ParentId, x.Name }).ToListAsync(cancellationToken);
        var elementRows = AccountRegisterHierarchy.Build(elements.Select(x => (x.Id, x.ParentId, x.Name)));
        var areaRows = AccountRegisterHierarchy.Build(areas.Select(x => (x.Id, x.ParentId, x.Name)));
        var details = new AgreementDetailsDto
        {
            DeadlinesInitialized = a.DeadlinesInitialized,
            Direction = a.Direction, Currency = a.Currency, IndexId = a.IndexId, IndexSetupNeedsReview = a.IndexSetupNeedsReview,
            IndexName = await db.AgreementIndexRecords.Where(x => x.AccountId == accountId && x.Id == a.IndexId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken),
            Id = a.Id, Title = a.Title, Description = a.Description, Type = a.Type, Status = a.Status,
            CounterpartyOrganizationId = a.CounterpartyOrganizationId, OwnerUserId = a.OwnerUserId,
            StartDate = a.StartDate, EndDate = a.EndDate, NoticeDeadline = a.NoticeDeadline, RenewalDate = a.RenewalDate,
            Form = a.Form, NoticeMode = a.NoticeMode, NoticeCount = a.NoticeCount, NoticeUnit = a.NoticeUnit,
            CurrentPeriodStartDate = a.CurrentPeriodStartDate,
            AutoRenew = a.AutoRenew, RenewalMonths = a.RenewalMonths, Terms = a.Terms,
            OrganizationElementId = a.OrganizationElementId, GeographicAreaId = a.GeographicAreaId,
            OrganizationElementName = elementRows.SingleOrDefault(x => x.Id == a.OrganizationElementId)?.Path,
            GeographicAreaName = areaRows.SingleOrDefault(x => x.Id == a.GeographicAreaId)?.Path,
            BuildingId = a.BuildingId, UnitId = a.UnitId, Revision = a.Revision,
            CreatedUtc = a.CreatedUtc, UpdatedUtc = a.UpdatedUtc,
            OwnerName = names.GetValueOrDefault(a.OwnerUserId, string.Empty), CreatedByName = names.GetValueOrDefault(a.CreatedByUserId, string.Empty), UpdatedByName = names.GetValueOrDefault(a.UpdatedByUserId, string.Empty),
            CanEdit = scope.CanEdit && !a.IsArchived, CanManageAccess = scope.CanManageAccess, IsArchived = a.IsArchived,
            CounterpartyName = await db.Organizations.Where(x => x.AccountId == accountId && x.Id == a.CounterpartyOrganizationId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) ?? string.Empty,
            BuildingName = await db.Buildings.Where(x => x.AccountId == accountId && x.Id == a.BuildingId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken),
            UnitName = await db.Units.Where(x => x.AccountId == accountId && x.Id == a.UnitId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
        };
        var members = db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id));
        details.Documents = await (from d in db.AgreementDocuments.AsNoTracking()
                                   join u in members on d.UploadedByUserId equals u.Id into uploaders
                                   from u in uploaders.DefaultIfEmpty()
                                   where d.AccountId == accountId && d.AgreementId == agreementId
                                   orderby d.UploadedUtc descending
                                   select new AgreementDocumentDto(d.Id, d.OriginalFileName, d.Size, d.Category,
                                       d.Description, d.UploadedUtc, u == null ? string.Empty : u.FirstName + " " + u.LastName)).ToListAsync(cancellationToken);
        if (scope.CanManageAccess)
            details.Access = await (from g in db.AgreementAccess.AsNoTracking()
                                    join u in members on g.UserId equals u.Id into grantees
                                    from u in grantees.DefaultIfEmpty()
                                    where g.AccountId == accountId && g.AgreementId == agreementId
                                    orderby u.FirstName, u.LastName
                                    select new AgreementAccessDto(g.UserId, u == null ? string.Empty : u.FirstName + " " + u.LastName, g.Level)).ToListAsync(cancellationToken);
        details.NoticeSnapshot = AgreementNoticeSnapshot.From(a);
        details.TerminationRegisteredByName = a.TerminationRegisteredByUserId is Guid registered ? names.GetValueOrDefault(registered) : null;
        var history = await (from h in db.AgreementNoticeHistory.AsNoTracking()
            join u in members on h.ActorUserId equals u.Id into actors
            from u in actors.DefaultIfEmpty()
            where h.AccountId == accountId && h.AgreementId == agreementId
            orderby h.CreatedUtc descending, h.Id
            select new { History = h, Actor = u == null ? string.Empty : u.FirstName + " " + u.LastName }).ToListAsync(cancellationToken);
        details.NoticeHistory = history.Select(x => new AgreementNoticeHistoryDto(x.History.Action, x.History.CreatedUtc,
            x.Actor, x.History.Comment, JsonSerializer.Deserialize<AgreementNoticeSnapshot>(x.History.BeforeJson)!,
            JsonSerializer.Deserialize<AgreementNoticeSnapshot>(x.History.AfterJson)!)).ToList();
        return details;
    }
}
