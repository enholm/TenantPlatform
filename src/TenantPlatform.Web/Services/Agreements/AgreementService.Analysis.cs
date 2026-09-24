using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public const int MaxAnalysisFiles = 10;
    public const long MaxAnalysisTotalBytes = 40 * 1024 * 1024;
    public long MaxAnalysisFileSizeBytes => Math.Min(MaxFileSizeBytes, 20 * 1024 * 1024);

    private async Task RequireAnalysisAccessAsync(TenantPlatformDbContext db, Guid accountId, Guid? agreementId, CancellationToken ct)
    {
        if (agreementId.HasValue)
        {
            var access = await RequireAsync(db, accountId, agreementId.Value, true, false, ct);
            // Access checks must not leave a stale tracked agreement before the later write lock.
            db.Entry(access.Agreement).State = EntityState.Detached;
        }
        else if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
    }
    private async Task<AgreementAnalysis> LockDraftAsync(TenantPlatformDbContext db, Guid accountId, Guid id, CancellationToken ct,
        bool allowApproved = false, bool discard = false)
    {
        await RequireMemberAsync(db, accountId, ct);
        var userId = userContext.Current.UserId;
        var draft = await db.AgreementAnalyses.FromSqlInterpolated($"SELECT * FROM agreement_analyses WHERE \"AccountId\" = {accountId} AND \"Id\" = {id} AND \"CreatedByUserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
        if (!discard) await RequireAnalysisAccessAsync(db, accountId, draft.AgreementId, ct);
        if (allowApproved && draft.State == AgreementAnalysisState.Approved) return draft;
        if (draft.State == AgreementAnalysisState.Approved || !discard && (draft.State == AgreementAnalysisState.Discarded || draft.ExpiresUtc <= Clock.GetUtcNow()))
            throw new AgreementValidationException("AnalysisExpired");
        if (draft.State == AgreementAnalysisState.Analyzing && draft.LeaseUntilUtc > Clock.GetUtcNow())
            throw new AgreementValidationException("AnalysisBusy");
        await db.Entry(draft).Collection(x => x.Files).LoadAsync(ct);
        return draft;
    }
    private static AnalysisDraftDto DraftDto(AgreementAnalysis draft) => new(draft.Id,
        draft.Files.Where(x => !x.PendingDelete).OrderBy(x => x.Category).Select(x => new AnalysisFileDto(x.Id, x.FileName, x.Size, x.Category)).ToList(),
        draft.OriginalResultJson is null ? null : JsonSerializer.Deserialize<ContractAnalysisResult>(draft.OriginalResultJson, ContractAnalysisJson.Options));

    public async Task<AnalysisDraftDto> StartAnalysisDraftAsync(Guid accountId, Guid? agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAnalysisAccessAsync(db, accountId, agreementId, ct);
        var draft = new AgreementAnalysis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            CreatedByUserId = userContext.Current.UserId, CreatedUtc = Clock.GetUtcNow(), ExpiresUtc = Clock.GetUtcNow().AddHours(24),
            State = AgreementAnalysisState.Draft, SchemaVersion = ContractAnalysisSchema.Version };
        if (agreementId.HasValue)
            draft.ExpectedAgreementRevision = (await RequireAsync(db, accountId, agreementId.Value, true, false, ct)).Agreement.Revision;
        db.AgreementAnalyses.Add(draft);
        await db.SaveChangesAsync(ct);
        return DraftDto(draft);
    }

    public async Task<AnalysisDraftDto> AddAnalysisFileAsync(Guid accountId, Guid draftId, string fileName, Stream content,
        AgreementDocumentCategory category, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(category) || !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new AgreementValidationException("AnalysisPdfOnly");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var draft = await LockDraftAsync(db, accountId, draftId, ct);
        var files = draft.Files.Where(x => !x.PendingDelete).ToList();
        if (files.Count >= MaxAnalysisFiles || category == AgreementDocumentCategory.Contract && files.Any(x => x.Category == category))
            throw new AgreementValidationException("AnalysisFileLimit");
        // The existing store validates the filename, PDF header, EOF and configured file size.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var stored = await storage.StoreAsync(content, fileName, timeout.Token);
        try
        {
            if (stored.Size > MaxAnalysisFileSizeBytes || stored.Size + files.Sum(x => x.Size) > MaxAnalysisTotalBytes)
                throw new AgreementValidationException("AnalysisFileLimit");
            // Access may have changed during streaming.
            await RequireAnalysisAccessAsync(db, accountId, draft.AgreementId, ct);
            draft.Files.Add(new AgreementAnalysisFile { Id = Guid.NewGuid(), AccountId = accountId, AnalysisId = draftId,
                FileName = stored.FileName, StorageKey = stored.StorageKey, MediaType = stored.MediaType, Size = stored.Size, Category = category });
            db.AgreementAnalysisFiles.Add(draft.Files.Last());
            draft.State = AgreementAnalysisState.Draft; draft.OriginalResultJson = null;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return DraftDto(draft);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            await using var check = await factory.CreateDbContextAsync(CancellationToken.None);
            if (!await check.AgreementAnalysisFiles.AnyAsync(x => x.AccountId == accountId && x.StorageKey == stored.StorageKey))
                await storage.DiscardUncommittedAsync(stored.StorageKey);
            throw;
        }
    }
    public async Task<AnalysisDraftDto> RemoveAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var draft = await LockDraftAsync(db, accountId, draftId, ct);
        var file = draft.Files.SingleOrDefault(x => x.Id == fileId) ?? throw new UnauthorizedAccessException();
        file.PendingDelete = true; draft.State = AgreementAnalysisState.Draft; draft.OriginalResultJson = null;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await new AgreementAnalysisCleanup(factory, storage, Clock).CleanDraftAsync(accountId, draftId, ct);
        return DraftDto(draft);
    }
    public async Task<AnalysisDraftDto> AnalyzeAsync(Guid accountId, Guid draftId, CancellationToken ct = default)
    {
        var client = analysisClient ?? throw new AgreementValidationException("AnalysisNotConfigured");
        var attempt = Guid.NewGuid();
        List<AgreementAnalysisFile> files;
        string ownBusiness;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var draft = await LockDraftAsync(db, accountId, draftId, ct);
            if (draft.State == AgreementAnalysisState.Ready) return DraftDto(draft);
            files = draft.Files.Where(x => !x.PendingDelete).ToList();
            if (files.Count(x => x.Category == AgreementDocumentCategory.Contract) != 1) throw new AgreementValidationException("AnalysisMainRequired");
            ownBusiness = await db.Accounts.Where(x => x.Id == accountId).Select(x => x.Name).SingleAsync(ct);
            draft.State = AgreementAnalysisState.Analyzing; draft.AttemptId = attempt;
            draft.LeaseUntilUtc = Clock.GetUtcNow().AddMinutes(10);
            db.AgreementAiUsage.Add(new AgreementAiUsage { Id = attempt, AccountId = accountId, AnalysisId = draftId,
                UserId = userContext.Current.UserId, CreatedUtc = Clock.GetUtcNow(), Model = client.Model });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var reply = await client.AnalyzeAsync(ownBusiness, files, timeout.Token);
            // Commit usage independently, before parsing results or checking current permissions.
            await using (var usageDb = await factory.CreateDbContextAsync(CancellationToken.None))
            {
                var usage = await usageDb.AgreementAiUsage.SingleAsync(x => x.AccountId == accountId && x.Id == attempt);
                usage.ResponseId = reply.ResponseId; usage.Model = reply.Model; usage.Status = reply.Status;
                usage.InputTokens = reply.InputTokens; usage.CachedInputTokens = reply.CachedInputTokens; usage.OutputTokens = reply.OutputTokens;
                await usageDb.SaveChangesAsync();
            }
            if (reply.Status != "completed" || reply.Json is null) throw new AgreementValidationException("AnalysisApiFailed");
            ContractAnalysisSchema.Parse(reply.Json, files.Select(x => x.Id).ToArray());
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Only the lease owner can publish the response. Do not use LockDraftAsync's busy guard here.
            var draft = await db.AgreementAnalyses.FromSqlInterpolated($"SELECT * FROM agreement_analyses WHERE \"AccountId\" = {accountId} AND \"Id\" = {draftId} FOR UPDATE").SingleAsync(ct);
            await RequireAnalysisAccessAsync(db, accountId, draft.AgreementId, ct);
            if (draft.AttemptId != attempt || draft.State != AgreementAnalysisState.Analyzing || draft.ExpiresUtc <= Clock.GetUtcNow())
                throw new AgreementValidationException("AnalysisExpired");
            draft.OriginalResultJson = reply.Json; draft.Model = reply.Model; draft.State = AgreementAnalysisState.Ready;
            draft.LeaseUntilUtc = null;
            await db.Entry(draft).Collection(x => x.Files).LoadAsync(ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return DraftDto(draft);
        }
        catch
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            // Raw updates avoid a stale worker resetting a newer attempt.
            await db.AgreementAnalyses.Where(x => x.AccountId == accountId && x.Id == draftId && x.AttemptId == attempt && x.State == AgreementAnalysisState.Analyzing)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AgreementAnalysisState.Draft).SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null));
            await db.AgreementAiUsage.Where(x => x.AccountId == accountId && x.Id == attempt && x.Status == "Started")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "FailedOrUnknown"));
            throw;
        }
    }

    public static string NormalizeOrganizationName(string value) => string.Join(' ', value.Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static string NormalizeOrganizationNumber(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static List<Organization> MatchOrganizations(List<Organization> organizations, string name, string number)
    {
        var normalizedNumber = NormalizeOrganizationNumber(number);
        if (normalizedNumber.Length > 0)
        {
            var byNumber = organizations.Where(x => NormalizeOrganizationNumber(x.OrganizationNumber) == normalizedNumber).ToList();
            if (byNumber.Count > 0) return byNumber;
        }
        var normalizedName = NormalizeOrganizationName(name);
        return organizations.Where(x => NormalizeOrganizationName(x.Name) == normalizedName).ToList();
    }
    public async Task<List<AnalysisOrganizationDto>> FindAnalysisOrganizationsAsync(Guid accountId, Guid draftId, string name, string number, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockDraftAsync(db, accountId, draftId, ct);
        var orgs = await db.Organizations.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct);
        return MatchOrganizations(orgs, name, number).Select(x => new AnalysisOrganizationDto(x.Id, x.Name, x.OrganizationNumber)).ToList();
    }

    public async Task<Guid> ApproveAnalysisAsync(Guid accountId, Guid draftId, ApproveAnalysisRequest request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var draft = await LockDraftAsync(db, accountId, draftId, ct, allowApproved: true);
        if (draft.State == AgreementAnalysisState.Approved) return draft.AgreementId!.Value;
        if (draft.State != AgreementAnalysisState.Ready || draft.OriginalResultJson is null) throw new AgreementValidationException("AnalysisRequired");
        var files = draft.Files.Where(x => !x.PendingDelete).ToList();
        var result = ContractAnalysisSchema.Parse(draft.OriginalResultJson, files.Select(x => x.Id).ToArray());
        if (result.Documents.Any(x => !x.Processed)) throw new AgreementValidationException("AnalysisIncomplete");
        if (!request.CounterpartyConfirmed || string.IsNullOrWhiteSpace(request.CounterpartyName) || request.CounterpartyName.Trim().Length > 200 ||
            request.OrganizationNumber.Length > 50 || request.Address.Length > 1000 || !Enum.IsDefined(request.OrganizationType) ||
            request.Findings.Count != result.Findings.Count || request.Findings.Any(x => !Enum.IsDefined(x.Status) || x.Value.Length > 10000 || x.Parties.Length > 2000))
            throw new AgreementValidationException("AnalysisReviewRequired");
        // Serialize approvals in this account: a second approval sees the first committed organization.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
        var orgs = await db.Organizations.Where(x => x.AccountId == accountId).ToListAsync(ct);
        Organization organization;
        if (request.OrganizationId.HasValue)
            organization = orgs.SingleOrDefault(x => x.Id == request.OrganizationId.Value) ?? throw new UnauthorizedAccessException();
        else
        {
            var matches = MatchOrganizations(orgs, request.CounterpartyName, request.OrganizationNumber);
            // A changed match requires explicit review; never silently choose even an exact name match.
            if (matches.Count > 0) throw new AgreementValidationException("AnalysisOrganizationChanged");
            organization = new Organization { Id = Guid.NewGuid(), AccountId = accountId, Name = request.CounterpartyName.Trim(),
                OrganizationNumber = string.IsNullOrWhiteSpace(request.OrganizationNumber) ? null : request.OrganizationNumber.Trim(), Type = request.OrganizationType };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(ct);
        }
        Agreement agreement;
        var userId = userContext.Current.UserId;
        if (draft.AgreementId.HasValue)
        {
            await LockFinancialAsync(db, accountId, draft.AgreementId.Value, ct);
            agreement = (await RequireAsync(db, accountId, draft.AgreementId.Value, true, false, ct)).Agreement;
            CheckRevision(agreement, draft.ExpectedAgreementRevision!.Value);
            if (agreement.CounterpartyOrganizationId != organization.Id && await db.AgreementLines.AnyAsync(x => x.AccountId == accountId && x.AgreementId == agreement.Id && x.ActivatedUtc != null, ct))
                throw new AgreementValidationException("FinanceAgreementLocked");
            agreement.CounterpartyOrganizationId = organization.Id;
        }
        else
        {
            var r = request.Agreement;
            r.CounterpartyOrganizationId = organization.Id;
            await ValidateAsync(db, accountId, r, ct);
            if (r.Form == AgreementForm.Legacy || r.BeginNewPeriod) throw new AgreementValidationException("NoticeChooseForm");
            r.Direction ??= r.Type == AgreementType.Supplier ? AgreementDirection.Cost : null;
            agreement = new Agreement { Id = Guid.NewGuid(), AccountId = accountId, CreatedUtc = Clock.GetUtcNow(), CreatedByUserId = userId, CurrentPeriodStartDate = r.StartDate };
            Apply(agreement, r);
            db.Agreements.Add(agreement);
            NoticeHistory(db, agreement, new AgreementNoticeSnapshot(), AgreementNoticeAction.RuleChanged);
            if (agreement.IndexId.HasValue) AddIndexSelection(db, agreement, agreement.IndexId, 1, agreement.StartDate);
            await AgreementDeadlineSynchronizer.SynchronizeAsync(db, agreement, Clock.GetUtcNow(), userId, ct);
        }
        Touch(agreement, userId);
        foreach (var file in files)
        {
            // Promotion is a metadata operation: no file copy/move can partially fail during commit.
            var doc = new AgreementDocument { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreement.Id,
                OriginalFileName = file.FileName, StorageKey = file.StorageKey, MediaType = file.MediaType, Size = file.Size,
                Category = file.Category, UploadedUtc = Clock.GetUtcNow(), UploadedByUserId = userId };
            db.AgreementDocuments.Add(doc); file.AgreementDocumentId = doc.Id;
        }
        for (var i = 0; i < result.Findings.Count; i++)
        {
            var original = result.Findings[i]; var correction = request.Findings[i];
            var finding = new AgreementFinding { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreement.Id, AnalysisId = draftId,
                Position = i, Category = original.Category, Status = correction.Status, OriginalStatus = original.Status, OriginalValue = original.Value,
                AdjustedValue = correction.Value == original.Value ? null : correction.Value, Parties = correction.Parties,
                OriginalParties = original.Parties, Explanation = original.Explanation };
            foreach (var source in original.Sources)
                finding.Sources.Add(new AgreementFindingSource { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreement.Id, FindingId = finding.Id,
                    DocumentId = files.Single(x => x.Id == source.DocumentId).AgreementDocumentId!.Value, Quote = source.Quote, Section = source.Section, Page = source.Page });
            db.AgreementFindings.Add(finding);
        }
        draft.AgreementId = agreement.Id; draft.State = AgreementAnalysisState.Approved;
        draft.ApprovedUtc = Clock.GetUtcNow(); draft.ApprovedByUserId = userId;
        draft.ApprovedOrganizationId = organization.Id; draft.ApprovedCounterpartyName = request.CounterpartyName.Trim();
        draft.ApprovedOrganizationNumber = request.OrganizationNumber.Trim(); draft.ApprovedAddress = request.Address.Trim();
        await SaveAsync(db, ct); await tx.CommitAsync(ct);
        return agreement.Id;
    }
    public async Task DiscardAnalysisAsync(Guid accountId, Guid draftId, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await RequireMemberAsync(db, accountId, ct);
            if (!await db.AgreementAnalyses.AnyAsync(x => x.AccountId == accountId && x.Id == draftId && x.CreatedByUserId == userContext.Current.UserId, ct)) return;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var draft = await LockDraftAsync(db, accountId, draftId, ct, discard: true);
            draft.State = AgreementAnalysisState.Discarded; draft.OriginalResultJson = null;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await new AgreementAnalysisCleanup(factory, storage, Clock).CleanDraftAsync(accountId, draftId, ct);
    }
    public async Task<AgreementDownload> DownloadAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var draft = await LockDraftAsync(db, accountId, draftId, ct);
        var file = draft.Files.SingleOrDefault(x => x.Id == fileId && !x.PendingDelete) ?? throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(file.StorageKey, ct), file.FileName, file.MediaType);
    }
    public async Task<List<AgreementAnalysis>> GetAnalysesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAsync(db, accountId, agreementId, false, false, ct);
        return await db.AgreementAnalyses.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.State == AgreementAnalysisState.Approved)
            .Include(x => x.Findings).ThenInclude(x => x.Sources).Include(x => x.Files)
            .AsSplitQuery().OrderByDescending(x => x.ApprovedUtc).ToListAsync(ct);
    }
}
