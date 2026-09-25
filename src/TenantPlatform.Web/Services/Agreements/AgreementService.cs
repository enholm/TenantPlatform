using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization,
    IAgreementDocumentStorage storage,
    ILogger<AgreementService> logger,
    TimeProvider? clock = null,
    Microsoft.Extensions.Options.IOptions<AgreementReminderOptions>? reminderOptions = null,
    IContractAnalysisClient? analysisClient = null) : IAgreementService, IAgreementFollowupService, IAgreementAnalysisService
{
    private TimeProvider Clock => clock ?? TimeProvider.System;
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;

    private async Task<(Guid UserId, bool Admin)> RequireMemberAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken ct)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !await db.UserAccounts.AnyAsync(x => x.AccountId == accountId && x.UserId == current.UserId && x.User.IsActive, ct))
            throw new UnauthorizedAccessException();
        return (current.UserId, await authorization.CanCreateAgreementAsync(ct));
    }

    private static IQueryable<Agreement> Accessible(TenantPlatformDbContext db, Guid accountId, Guid userId, bool admin) =>
        db.Agreements.Where(x => x.AccountId == accountId &&
            (admin || x.OwnerUserId == userId || db.AgreementAccess.Any(g =>
                g.AccountId == accountId && g.AgreementId == x.Id && g.UserId == userId)));

    private async Task<(Agreement Agreement, bool Edit, bool Manage)> RequireAsync(
        TenantPlatformDbContext db, Guid accountId, Guid id, bool edit, bool manage, CancellationToken ct)
    {
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var agreement = await Accessible(db, accountId, userId, admin).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new UnauthorizedAccessException();
        var canManage = admin || agreement.OwnerUserId == userId;
        var canEdit = canManage || await db.AgreementAccess.AnyAsync(x => x.AccountId == accountId &&
            x.AgreementId == id && x.UserId == userId && x.Level == AgreementAccessLevel.Edit, ct);
        if ((edit && !canEdit) || (manage && !canManage)) throw new UnauthorizedAccessException();
        if (edit && agreement.IsArchived) throw new AgreementValidationException("FollowupArchived");
        return (agreement, canEdit, canManage);
    }

    public async Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var (userId, admin) = await RequireMemberAsync(db, accountId, cancellationToken);
        var visible = Accessible(db, accountId, userId, admin).AsNoTracking();
        if (filter.Attention.HasValue)
        {
            var today = AgreementReminderSchedule.Today(Clock.GetUtcNow(),
                (await SettingsAsync(db, accountId, cancellationToken)).TimeZoneId);
            visible = AgreementDashboardQueries.Attention(visible, filter.Attention.Value, today);
        }
        var owners = await (from a in visible join u in db.Users on a.OwnerUserId equals u.Id
                            select new { u.Id, Name = u.FirstName + " " + u.LastName })
            .Distinct().OrderBy(x => x.Name)
            .Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        var query = from a in visible
                    join org in db.Organizations on a.CounterpartyOrganizationId equals org.Id
                    join owner in db.Users on a.OwnerUserId equals owner.Id
                    where org.AccountId == accountId
                    select new { Agreement = a, Counterparty = org.Name, Owner = owner.FirstName + " " + owner.LastName };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Agreement.Title.ToLower().Contains(search) || x.Counterparty.ToLower().Contains(search));
        }
        if (filter.Status.HasValue) query = query.Where(x => x.Agreement.Status == filter.Status.Value);
        if (filter.Type.HasValue) query = query.Where(x => x.Agreement.Type == filter.Type.Value);
        if (filter.OwnerUserId.HasValue) query = query.Where(x => x.Agreement.OwnerUserId == filter.OwnerUserId.Value);
        const int pageSize = 25;
        var count = await query.CountAsync(cancellationToken);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var items = await query.OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AgreementListItemDto(x.Agreement.Id, x.Agreement.Title, x.Counterparty, x.Agreement.Type,
                x.Agreement.Status, x.Agreement.OwnerUserId, x.Owner, x.Agreement.EndDate, x.Agreement.NoticeDeadline, x.Agreement.IsArchived))
            .ToListAsync(cancellationToken);
        return new(items, count, page, pageSize, owners);
    }

    public async Task<AgreementDetailsDto> GetAsync(Guid accountId, Guid agreementId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var (a, edit, manage) = await RequireAsync(db, accountId, agreementId, false, false, cancellationToken);
        if (!a.DeadlinesInitialized)
        {
            await new AgreementDeadlineInitializer(factory, Clock).EnsureAccountAsync(accountId, cancellationToken);
            await db.Entry(a).ReloadAsync(cancellationToken);
        }
        var names = await db.Users.Where(x => x.Id == a.OwnerUserId || x.Id == a.CreatedByUserId || x.Id == a.UpdatedByUserId)
            .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        var structure = await GetStructureOptionsAsync(db, accountId, a, true, cancellationToken);
        var details = new AgreementDetailsDto
        {
            Direction = a.Direction, Currency = a.Currency, IndexId = a.IndexId, IndexSetupNeedsReview = a.IndexSetupNeedsReview,
            IndexName = await db.AgreementIndexRecords.Where(x => x.AccountId == accountId && x.Id == a.IndexId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken),
            Id = a.Id, Title = a.Title, Description = a.Description, Type = a.Type, Status = a.Status,
            CounterpartyOrganizationId = a.CounterpartyOrganizationId, OwnerUserId = a.OwnerUserId,
            StartDate = a.StartDate, EndDate = a.EndDate, NoticeDeadline = a.NoticeDeadline, RenewalDate = a.RenewalDate,
            Form = a.Form, NoticeMode = a.NoticeMode, NoticeCount = a.NoticeCount, NoticeUnit = a.NoticeUnit,
            CurrentPeriodStartDate = a.CurrentPeriodStartDate,
            AutoRenew = a.AutoRenew, RenewalMonths = a.RenewalMonths, Terms = a.Terms,
            OrganizationElementId = a.OrganizationElementId, GeographicAreaId = a.GeographicAreaId,
            OrganizationElementName = structure.Elements.SingleOrDefault(x => x.Id == a.OrganizationElementId)?.Name,
            GeographicAreaName = structure.Areas.SingleOrDefault(x => x.Id == a.GeographicAreaId)?.Name,
            BuildingId = a.BuildingId, UnitId = a.UnitId, Revision = a.Revision,
            CreatedUtc = a.CreatedUtc, UpdatedUtc = a.UpdatedUtc,
            OwnerName = names[a.OwnerUserId], CreatedByName = names[a.CreatedByUserId], UpdatedByName = names[a.UpdatedByUserId],
            CanEdit = edit && !a.IsArchived, CanManageAccess = manage, IsArchived = a.IsArchived,
            CounterpartyName = await db.Organizations.Where(x => x.AccountId == accountId && x.Id == a.CounterpartyOrganizationId)
                .Select(x => x.Name).SingleAsync(cancellationToken),
            BuildingName = await db.Buildings.Where(x => x.AccountId == accountId && x.Id == a.BuildingId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken),
            UnitName = await db.Units.Where(x => x.AccountId == accountId && x.Id == a.UnitId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
        };
        details.Documents = await (from d in db.AgreementDocuments.AsNoTracking()
                                   join u in db.Users on d.UploadedByUserId equals u.Id
                                   where d.AccountId == accountId && d.AgreementId == agreementId
                                   orderby d.UploadedUtc descending
                                   select new AgreementDocumentDto(d.Id, d.OriginalFileName, d.Size, d.Category,
                                       d.Description, d.UploadedUtc, u.FirstName + " " + u.LastName)).ToListAsync(cancellationToken);
        if (manage)
            details.Access = await (from g in db.AgreementAccess.AsNoTracking()
                                    join u in db.Users on g.UserId equals u.Id
                                    where g.AccountId == accountId && g.AgreementId == agreementId
                                    orderby u.FirstName, u.LastName
                                    select new AgreementAccessDto(u.Id, u.FirstName + " " + u.LastName, g.Level)).ToListAsync(cancellationToken);
        await LoadNoticeAsync(db, a, details, cancellationToken);
        return details;
    }

    public async Task<AgreementOptionsDto> GetOptionsAsync(Guid accountId, Guid? agreementId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        Agreement? current = null;
        if (agreementId.HasValue) current = (await RequireAsync(db, accountId, agreementId.Value, true, false, cancellationToken)).Item1;
        else if (!(await RequireMemberAsync(db, accountId, cancellationToken)).Admin) throw new UnauthorizedAccessException();
        var structure = await GetStructureOptionsAsync(db, accountId, current, false, cancellationToken);
        return new AgreementOptionsDto
        {
            OrganizationElements = structure.Elements, GeographicAreas = structure.Areas,
            Indices = await db.AgreementIndexRecords.Where(x => x.AccountId == accountId).OrderBy(x => x.Name).Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(cancellationToken),
            Counterparties = await db.Organizations.Where(x => x.AccountId == accountId).OrderBy(x => x.Name)
                .Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(cancellationToken),
            Members = await db.UserAccounts.Where(x => x.AccountId == accountId && x.User.IsActive).OrderBy(x => x.User.FirstName)
                .Select(x => new AgreementOptionDto(x.UserId, x.User.FirstName + " " + x.User.LastName, null)).ToListAsync(cancellationToken),
            Buildings = await db.Buildings.Where(x => x.AccountId == accountId).OrderBy(x => x.Name)
                .Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(cancellationToken),
            Units = await db.Units.Where(x => x.AccountId == accountId).OrderBy(x => x.Name)
                .Select(x => new AgreementOptionDto(x.Id, x.Name, x.BuildingId)).ToListAsync(cancellationToken)
        };
    }

    public async Task<Guid> CreateAsync(Guid accountId, SaveAgreementRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var (userId, admin) = await RequireMemberAsync(db, accountId, cancellationToken);
        if (!admin) throw new UnauthorizedAccessException();
        await ValidateAsync(db, accountId, request, cancellationToken);
        var a = new Agreement { Id = Guid.NewGuid(), AccountId = accountId, CreatedUtc = Clock.GetUtcNow(), CreatedByUserId = userId };
        if (request.Form == AgreementForm.Legacy || request.BeginNewPeriod)
            throw new AgreementValidationException("NoticeChooseForm");
        request.Direction ??= request.Type == AgreementType.Supplier ? AgreementDirection.Cost : null;
        a.CurrentPeriodStartDate = request.StartDate;
        Apply(a, request); Touch(a, userId);
        NoticeHistory(db, a, new AgreementNoticeSnapshot(), AgreementNoticeAction.RuleChanged);
        db.Agreements.Add(a);
        if (a.IndexId.HasValue) AddIndexSelection(db, a, a.IndexId, 1, a.StartDate);
        await AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, Clock.GetUtcNow(), userId, cancellationToken);
        await SaveAsync(db, cancellationToken);
        return a.Id;
    }

    public async Task UpdateAsync(Guid accountId, Guid agreementId, SaveAgreementRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFinancialAsync(db, accountId, agreementId, cancellationToken);
        var (a, _, manage) = await RequireAsync(db, accountId, agreementId, true, false, cancellationToken);
        CheckRevision(a, request.Revision);
        if (!manage && request.OwnerUserId != a.OwnerUserId) throw new UnauthorizedAccessException();
        await ValidateAsync(db, accountId, request, cancellationToken, a);
        if (request.Form == AgreementForm.Legacy && a.Form != AgreementForm.Legacy)
            throw new AgreementValidationException("NoticeChooseForm");
        if ((a.Direction != request.Direction || a.Currency != request.Currency || a.CounterpartyOrganizationId != request.CounterpartyOrganizationId) &&
            await db.AgreementLines.AnyAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.ActivatedUtc != null, cancellationToken))
            throw new AgreementValidationException("FinanceAgreementLocked");
        var before = AgreementNoticeSnapshot.From(a);
        if (request.BeginNewPeriod)
        {
            if (a.Form != AgreementForm.Renewing || request.Form != AgreementForm.Renewing ||
                !request.NewPeriodStartDate.HasValue || request.NewPeriodStartDate <= a.CurrentPeriodStartDate ||
                !request.RenewalDate.HasValue || request.RenewalDate <= request.NewPeriodStartDate)
                throw new AgreementValidationException("NoticeInvalidPeriod");
            a.CurrentPeriodStartDate = request.NewPeriodStartDate.Value;
        }
        if (!request.IndexId.HasValue && (!a.IndexSetupNeedsReview || request.ResolveIndexSetup) &&
            (await db.AgreementLineVersions.Where(x => x.AccountId == accountId && x.AgreementId == agreementId).ToListAsync(cancellationToken))
                .GroupBy(x => x.LineId).Any(g => g.MaxBy(x => x.Sequence)!.IndexRegulated))
            throw new AgreementValidationException("SimpleIndexRequired");
        if (a.IndexId != request.IndexId || request.ResolveIndexSetup)
            foreach (var proposal in await db.AgreementAdjustmentProposalRecords.Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Status == AgreementProposalStatus.Pending).ToListAsync(cancellationToken))
                proposal.Status = AgreementProposalStatus.Stale;
        if (a.IndexId != request.IndexId)
        {
            var last = await db.AgreementIndexSelections.Where(x => x.AccountId == accountId && x.AgreementId == agreementId)
                .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(cancellationToken);
            var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
            AddIndexSelection(db, a, request.IndexId, (last?.Sequence ?? 0) + 1,
                last is null ? a.StartDate : today > a.StartDate ? today : a.StartDate);
        }
        var previousOwner = a.OwnerUserId;
        Apply(a, request); Touch(a, userContext.Current.UserId);
        NoticeHistory(db, a, before, request.BeginNewPeriod ? AgreementNoticeAction.PeriodStarted : AgreementNoticeAction.RuleChanged);
        await AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, Clock.GetUtcNow(), userContext.Current.UserId, cancellationToken);
        if (previousOwner != a.OwnerUserId)
        {
            var inherited = await db.AgreementDeadlines.Where(x => x.AccountId == accountId && x.AgreementId == a.Id &&
                x.State == AgreementDeadlineState.Current && x.AssignedUserId == null).ToListAsync(cancellationToken);
            foreach (var deadline in inherited.Where(x => x.State == AgreementDeadlineState.Current))
            {
                deadline.UpdatedUtc = Clock.GetUtcNow(); deadline.UpdatedByUserId = userContext.Current.UserId;
                AgreementDeadlineSynchronizer.AddHistory(db, deadline, AgreementHistoryKind.Assigned,
                    Clock.GetUtcNow(), userContext.Current.UserId, effectiveAssignee: a.OwnerUserId);
            }
        }
        await SaveAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetAccessAsync(Guid accountId, Guid agreementId, Guid userId, AgreementAccessLevel? level, Guid revision, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, false, true, cancellationToken);
        CheckRevision(a, revision);
        if (level.HasValue && (!Enum.IsDefined(level.Value) || !await db.UserAccounts.AnyAsync(x =>
                x.AccountId == accountId && x.UserId == userId && x.User.IsActive, cancellationToken)))
            throw new AgreementValidationException("AgreementInvalidMember");
        var grant = await db.AgreementAccess.SingleOrDefaultAsync(x =>
            x.AccountId == accountId && x.AgreementId == agreementId && x.UserId == userId, cancellationToken);
        if (level is null) { if (grant is not null) db.AgreementAccess.Remove(grant); }
        else if (grant is null) db.AgreementAccess.Add(new AgreementAccess
        { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, UserId = userId, Level = level.Value });
        else grant.Level = level.Value;
        Touch(a, userContext.Current.UserId);
        await SaveAsync(db, cancellationToken);
    }

    public async Task UploadAsync(Guid accountId, Guid agreementId, Guid revision, string fileName, Stream content,
        AgreementDocumentCategory category, string? description, CancellationToken cancellationToken = default)
    {
        await using (var check = await factory.CreateDbContextAsync(cancellationToken))
        {
            var (a, _, _) = await RequireAsync(check, accountId, agreementId, true, false, cancellationToken);
            CheckRevision(a, revision);
        }
        if (!Enum.IsDefined(category) || description?.Length > 2000) throw new AgreementValidationException("AgreementInvalidDocument");
        var stored = await storage.StoreAsync(content, fileName, cancellationToken);
        try
        {
            // Recheck access after streaming: no DbContext is retained during the upload.
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, cancellationToken);
            CheckRevision(a, revision);
            db.AgreementDocuments.Add(new AgreementDocument
            {
                Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
                OriginalFileName = stored.FileName, StorageKey = stored.StorageKey, MediaType = stored.MediaType,
                Size = stored.Size, Category = category, Description = description?.Trim(),
                UploadedUtc = Clock.GetUtcNow(), UploadedByUserId = userContext.Current.UserId
            });
            Touch(a, userContext.Current.UserId);
            await SaveAsync(db, cancellationToken);
        }
        catch
        {
            // If a connection failed during commit, keep the file unless we can prove
            // no metadata exists. An orphan is safer than breaking a committed download.
            try
            {
                await using var check = await factory.CreateDbContextAsync(CancellationToken.None);
                if (!await check.AgreementDocuments.AnyAsync(x => x.AccountId == accountId && x.StorageKey == stored.StorageKey))
                    await storage.DiscardUncommittedAsync(stored.StorageKey);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not clean up uncommitted agreement file {StorageKey}.", stored.StorageKey); }
            throw;
        }
    }

    public async Task<AgreementDownload> DownloadAsync(Guid accountId, Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await RequireMemberAsync(db, accountId, cancellationToken);
        var doc = await db.AgreementDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == documentId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        await RequireAsync(db, accountId, doc.AgreementId, false, false, cancellationToken);
        return new(await storage.OpenReadAsync(doc.StorageKey, cancellationToken), doc.OriginalFileName, doc.MediaType);
    }

    private static void CheckRevision(Agreement a, Guid revision)
    {
        if (a.Revision != revision) throw new AgreementValidationException("AgreementConcurrencyConflict");
    }
    private void Touch(Agreement a, Guid userId)
    { a.Revision = Guid.NewGuid(); a.UpdatedUtc = Clock.GetUtcNow(); a.UpdatedByUserId = userId; }
    private static async Task SaveAsync(TenantPlatformDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new AgreementValidationException("AgreementConcurrencyConflict"); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" })
        { throw new AgreementValidationException("AgreementReferenceChanged"); }
    }
    private void AddIndexSelection(TenantPlatformDbContext db, Agreement a, Guid? indexId, int sequence, DateOnly date) =>
        db.AgreementIndexSelections.Add(new() { Id = Guid.NewGuid(), AccountId = a.AccountId, AgreementId = a.Id,
            IndexId = indexId, Sequence = sequence, EffectiveFrom = date, RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId });

    private static void Apply(Agreement a, SaveAgreementRequest r)
    {
        a.IndexId = r.IndexId;
        if (r.ResolveIndexSetup) a.IndexSetupNeedsReview = false;
        a.Direction = r.Direction; a.Currency = string.IsNullOrEmpty(r.Currency) ? null : r.Currency;
        a.Title = r.Title.Trim(); a.Description = r.Description?.Trim(); a.Type = r.Type;
        a.CounterpartyOrganizationId = r.CounterpartyOrganizationId; a.OwnerUserId = r.OwnerUserId; a.Status = r.Status;
        a.StartDate = r.StartDate; a.EndDate = r.EndDate; a.NoticeDeadline = r.NoticeDeadline;
        a.RenewalDate = r.RenewalDate;
        a.AutoRenew = r.AutoRenew; a.RenewalMonths = r.AutoRenew ? r.RenewalMonths : null; a.Terms = r.Terms?.Trim();
        a.OrganizationElementId = r.OrganizationElementId; a.GeographicAreaId = r.GeographicAreaId;
        a.BuildingId = r.BuildingId; a.UnitId = r.UnitId;
        AgreementNoticeRules.Apply(a, r);
    }
    private static async Task ValidateAsync(TenantPlatformDbContext db, Guid accountId, SaveAgreementRequest r, CancellationToken ct, Agreement? existing = null)
    {
        await ValidateStructureAsync(db, accountId, r, existing, ct);
        if (r.IndexId.HasValue && !await db.AgreementIndexRecords.AnyAsync(x => x.AccountId == accountId && x.Id == r.IndexId, ct))
            throw new AgreementValidationException("FinanceInvalidReference");
        if ((r.Direction.HasValue && !Enum.IsDefined(r.Direction.Value)) ||
            (!string.IsNullOrEmpty(r.Currency) && !AgreementPeriodCalculator.Currencies.Contains(r.Currency)))
            throw new AgreementValidationException("FinanceInvalidSettings");
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 200 || r.Description?.Length > 4000 || r.Terms?.Length > 10000 ||
            !Enum.IsDefined(r.Type) || !Enum.IsDefined(r.Status)) throw new AgreementValidationException("AgreementInvalidDetails");
        if (r.StartDate == default || (r.Form != AgreementForm.Ongoing && r.EndDate < r.StartDate)) throw new AgreementValidationException("AgreementInvalidDates");
        if ((r.Form is AgreementForm.Legacy or AgreementForm.Renewing) && r.RenewalMonths.HasValue && (!r.AutoRenew || r.RenewalMonths <= 0)) throw new AgreementValidationException("AgreementInvalidRenewal");
        if (!await db.Organizations.AnyAsync(x => x.AccountId == accountId && x.Id == r.CounterpartyOrganizationId, ct))
            throw new AgreementValidationException("AgreementInvalidCounterparty");
        if (!await db.UserAccounts.AnyAsync(x => x.AccountId == accountId && x.UserId == r.OwnerUserId && x.User.IsActive, ct))
            throw new AgreementValidationException("AgreementInvalidMember");
        if (r.BuildingId.HasValue && !await db.Buildings.AnyAsync(x => x.AccountId == accountId && x.Id == r.BuildingId, ct))
            throw new AgreementValidationException("AgreementInvalidBuilding");
        if (r.UnitId.HasValue && (!r.BuildingId.HasValue || !await db.Units.AnyAsync(x =>
                x.AccountId == accountId && x.Id == r.UnitId && x.BuildingId == r.BuildingId, ct)))
            throw new AgreementValidationException("AgreementInvalidUnit");
    }
}
