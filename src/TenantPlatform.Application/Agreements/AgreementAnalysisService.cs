using System.Text;
using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
namespace TenantPlatform.Application.Agreements;

public sealed class AgreementAnalysisService(IAuthorizationService authorization, IAgreementAnalysisData data,
    IAgreementDocumentStorage storage, AgreementManagementService management, AgreementAnalysisCleanupService cleanup,
    TimeProvider clock, IContractAnalysisClient? analysisClient = null) : IAgreementAnalysisService
{
    public const int MaxAnalysisFiles = 10;
    public const long MaxAnalysisTotalBytes = 40 * 1024 * 1024;
    public long MaxAnalysisFileSizeBytes => Math.Min(storage.MaxFileSizeBytes, 20 * 1024 * 1024);
    private async Task<Guid> Actor(Guid account, CancellationToken ct) => (await authorization.GetAccountActorScopeAsync(account, ct)).UserId;
    private async Task RequireAccess(Guid account, Guid? agreement, Guid user, CancellationToken ct)
    {
        if (agreement.HasValue)
        {
            var scope = await authorization.GetAgreementReadScopeAsync(account, agreement.Value, ct);
            if (scope.ListScope.UserId != user) throw new UnauthorizedAccessException();
            if (scope.IsArchived && scope.CanEditBeforeArchive) throw new AgreementValidationException("FollowupArchived");
            if (!scope.CanEdit) throw new UnauthorizedAccessException();
        }
        else
        {
            var actor = await authorization.GetAccountActorScopeAsync(account, ct);
            if (actor.UserId != user || !actor.Assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Agreements.Create) && !x.OrganizationId.HasValue && !x.BuildingId.HasValue)) throw new UnauthorizedAccessException();
        }
    }
    private void ValidateDraft(AgreementAnalysis draft, bool allowApproved = false, bool discard = false)
    {
        if (allowApproved && draft.State == AgreementAnalysisState.Approved) return;
        if (draft.State == AgreementAnalysisState.Approved || !discard && (draft.State == AgreementAnalysisState.Discarded || draft.ExpiresUtc <= clock.GetUtcNow())) throw new AgreementValidationException("AnalysisExpired");
        if (draft.State == AgreementAnalysisState.Analyzing && draft.LeaseUntilUtc > clock.GetUtcNow()) throw new AgreementValidationException("AnalysisBusy");
    }
    private sealed class Session(IAgreementAnalysisTransaction transaction, Guid user) : IAsyncDisposable
    {
        private bool disposed;
        public IAgreementAnalysisTransaction Transaction => transaction;
        public Guid User => user;
        public async ValueTask DisposeAsync() { if (!disposed) { disposed = true; await transaction.DisposeAsync(); } }
    }
    private async Task<Session> Open(Guid account, Guid id, CancellationToken ct, bool allowApproved = false, bool publish = false)
    {
        var user = await Actor(account, ct); var tx = await data.BeginAsync(account, id, user, ct);
        try
        {
            if (await Actor(account, ct) != user || tx.Draft is null) throw new UnauthorizedAccessException();
            await RequireAccess(account, tx.Draft.AgreementId, user, ct);
            if (!publish) ValidateDraft(tx.Draft, allowApproved);
            return new(tx, user);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    private static AnalysisDraftDto DraftDto(AgreementAnalysis draft) => new(draft.Id,
        draft.Files.Where(x => !x.PendingDelete).OrderBy(x => x.Category).Select(x => new AnalysisFileDto(x.Id, x.FileName, x.Size, x.Category)).ToList(),
        draft.OriginalResultJson is null ? null : JsonSerializer.Deserialize<ContractAnalysisResult>(draft.OriginalResultJson, ContractAnalysisJson.Options));

    public async Task<AnalysisDraftDto> StartAnalysisDraftAsync(Guid accountId, Guid? agreementId, CancellationToken ct = default)
    {
        var user = await Actor(accountId, ct);
        await RequireAccess(accountId, agreementId, user, ct);
        await using var tx = await data.BeginAsync(accountId, null, user, ct);
        await RequireAccess(accountId, agreementId, user, ct);
        var draft = new AgreementAnalysis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            CreatedByUserId = user, CreatedUtc = clock.GetUtcNow(), ExpiresUtc = clock.GetUtcNow().AddHours(24),
            State = AgreementAnalysisState.Draft, SchemaVersion = ContractAnalysisSchema.Version };
        if (agreementId.HasValue)
            draft.ExpectedAgreementRevision = await data.ReadAgreementRevisionAsync(accountId, agreementId.Value, ct);
        tx.Draft = draft;
        await RequireAccess(accountId, agreementId, user, ct);
        await tx.CommitAsync(ct);
        return DraftDto(draft);
    }

    public async Task<AnalysisDraftDto> AddAnalysisFileAsync(Guid accountId, Guid draftId, string fileName, Stream content,
        AgreementDocumentCategory category, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(category) || !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new AgreementValidationException("AnalysisPdfOnly");
        await using var session = await Open(accountId, draftId, ct);
        var tx = session.Transaction; var draft = tx.Draft!; var user = session.User;
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
            await RequireAccess(accountId, draft.AgreementId, user, ct);
            draft.Files.Add(new AgreementAnalysisFile { Id = Guid.NewGuid(), AccountId = accountId, AnalysisId = draftId,
                FileName = stored.FileName, StorageKey = stored.StorageKey, MediaType = stored.MediaType, Size = stored.Size, Category = category });
            draft.State = AgreementAnalysisState.Draft; draft.OriginalResultJson = null;
            await tx.CommitAsync(ct);
            return DraftDto(draft);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (!await data.HasFileMetadataAsync(accountId, stored.StorageKey, CancellationToken.None))
                await storage.DiscardUncommittedAsync(stored.StorageKey);
            throw;
        }
    }
    public async Task<AnalysisDraftDto> RemoveAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default)
    {
        await using var session = await Open(accountId, draftId, ct);
        var tx = session.Transaction; var draft = tx.Draft!;
        var file = draft.Files.SingleOrDefault(x => x.Id == fileId) ?? throw new UnauthorizedAccessException();
        file.PendingDelete = true; draft.State = AgreementAnalysisState.Draft; draft.OriginalResultJson = null;
        await tx.CommitAsync(ct);
        await session.DisposeAsync();
        await cleanup.CleanDraftAsync(accountId, draftId, ct);
        return DraftDto(draft);
    }
    public async Task<AnalysisDraftDto> AnalyzeAsync(Guid accountId, Guid draftId, CancellationToken ct = default)
    {
        IContractAnalysisClient client;
        var user = await Actor(accountId, ct);
        var attempt = Guid.NewGuid();
        List<AgreementAnalysisFile> files;
        string ownBusiness;
        await using (var session = await Open(accountId, draftId, ct))
        {
            if (session.User != user) throw new UnauthorizedAccessException();
            var tx = session.Transaction; var draft = tx.Draft!;
            client = analysisClient ?? throw new AgreementValidationException("AnalysisNotConfigured");
            if (draft.State == AgreementAnalysisState.Ready) return DraftDto(draft);
            files = draft.Files.Where(x => !x.PendingDelete).ToList();
            if (files.Count(x => x.Category == AgreementDocumentCategory.Contract) != 1) throw new AgreementValidationException("AnalysisMainRequired");
            ownBusiness = await tx.ReadAccountNameAsync(ct);
            draft.State = AgreementAnalysisState.Analyzing; draft.AttemptId = attempt;
            draft.LeaseUntilUtc = clock.GetUtcNow().AddMinutes(10);
            tx.Usage = new AgreementAiUsage { Id = attempt, AccountId = accountId, AnalysisId = draftId,
                UserId = user, CreatedUtc = clock.GetUtcNow(), Model = client.Model };
            await tx.CommitAsync(ct);
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var reply = await client.AnalyzeAsync(ownBusiness, files, timeout.Token);
            // Commit usage independently, before parsing results or checking current permissions.
            await data.RecordUsageAsync(accountId, draftId, attempt, reply, CancellationToken.None);
            if (reply.Status != "completed" || reply.Json is null) throw new AgreementValidationException("AnalysisApiFailed");
            ContractAnalysisSchema.Parse(reply.Json, files.Select(x => x.Id).ToArray());
            await using var session = await Open(accountId, draftId, ct, publish: true);
            if (session.User != user) throw new UnauthorizedAccessException();
            var tx = session.Transaction; var draft = tx.Draft!;
            if (draft.AttemptId != attempt || draft.State != AgreementAnalysisState.Analyzing || draft.ExpiresUtc <= clock.GetUtcNow())
                throw new AgreementValidationException("AnalysisExpired");
            draft.OriginalResultJson = reply.Json; draft.Model = reply.Model; draft.State = AgreementAnalysisState.Ready;
            draft.LeaseUntilUtc = null;
            await tx.CommitAsync(ct);
            return DraftDto(draft);
        }
        catch
        {
            await data.ResetAttemptAsync(accountId, draftId, attempt, CancellationToken.None);
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
        await using var session = await Open(accountId, draftId, ct);
        var orgs = await session.Transaction.ReadOrganizationsAsync(false, ct);
        return MatchOrganizations(orgs, name, number).Select(x => new AnalysisOrganizationDto(x.Id, x.Name, x.OrganizationNumber)).ToList();
    }

    public async Task<Guid> ApproveAnalysisAsync(Guid accountId, Guid draftId, ApproveAnalysisRequest request, CancellationToken ct = default)
    {
        request = JsonSerializer.Deserialize<ApproveAnalysisRequest>(JsonSerializer.Serialize(request))!;
        await using var session = await Open(accountId, draftId, ct, allowApproved: true);
        var tx = session.Transaction; var draft = tx.Draft!; var user = session.User;
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
        var orgs = await tx.ReadOrganizationsAsync(true, ct);
        await RequireAccess(accountId, draft.AgreementId, user, ct);
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
            await tx.PersistOrganizationAsync(organization, ct);
        }
        var approval = await tx.PrepareApprovalAsync(draft.AgreementId, ct);
        await RequireAccess(accountId, draft.AgreementId, user, ct);
        Agreement agreement;
        if (draft.AgreementId.HasValue)
        {
            agreement = approval.Agreement!;
            if (agreement.Revision != draft.ExpectedAgreementRevision) throw new AgreementValidationException("AgreementConcurrencyConflict");
            if (agreement.CounterpartyOrganizationId != organization.Id && approval.HasActivatedLines) throw new AgreementValidationException("FinanceAgreementLocked");
            agreement.CounterpartyOrganizationId = organization.Id;
        }
        else
        {
            request.Agreement.CounterpartyOrganizationId = organization.Id;
            agreement = management.PrepareCreate(approval, accountId, request.Agreement, user);
            await tx.SynchronizeApprovalAsync(clock.GetUtcNow(), user, ct);
        }
        agreement.Revision = Guid.NewGuid(); agreement.UpdatedUtc = clock.GetUtcNow(); agreement.UpdatedByUserId = user;
        foreach (var file in files)
        {
            // Promotion is a metadata operation: no file copy/move can partially fail during commit.
            var doc = new AgreementDocument { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreement.Id,
                OriginalFileName = file.FileName, StorageKey = file.StorageKey, MediaType = file.MediaType, Size = file.Size,
                Category = file.Category, UploadedUtc = clock.GetUtcNow(), UploadedByUserId = user };
            tx.Documents.Add(doc); file.AgreementDocumentId = doc.Id;
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
            draft.Findings.Add(finding);
        }
        draft.AgreementId = agreement.Id; draft.State = AgreementAnalysisState.Approved;
        draft.ApprovedUtc = clock.GetUtcNow(); draft.ApprovedByUserId = user;
        draft.ApprovedOrganizationId = organization.Id; draft.ApprovedCounterpartyName = request.CounterpartyName.Trim();
        draft.ApprovedOrganizationNumber = request.OrganizationNumber.Trim(); draft.ApprovedAddress = request.Address.Trim();
        await tx.CommitAsync(ct);
        return agreement.Id;
    }
    public async Task DiscardAnalysisAsync(Guid accountId, Guid draftId, CancellationToken ct = default)
    {
        var user = await Actor(accountId, ct);
        await using (var tx = await data.BeginAsync(accountId, draftId, user, ct))
        {
            if (await Actor(accountId, ct) != user) throw new UnauthorizedAccessException();
            if (tx.Draft is null) return;
            ValidateDraft(tx.Draft, discard: true);
            tx.Draft.State = AgreementAnalysisState.Discarded; tx.Draft.OriginalResultJson = null;
            await tx.CommitAsync(ct);
        }
        await cleanup.CleanDraftAsync(accountId, draftId, ct);
    }
    public async Task<AgreementDownload> DownloadAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default)
    {
        await using var session = await Open(accountId, draftId, ct);
        var file = session.Transaction.Draft!.Files.SingleOrDefault(x => x.Id == fileId && !x.PendingDelete) ?? throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(file.StorageKey, ct), file.FileName, file.MediaType);
    }
    public async Task<List<AgreementAnalysis>> GetAnalysesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await authorization.GetAgreementReadScopeAsync(accountId, agreementId, ct);
        var rows = await data.ReadApprovedAsync(accountId, agreementId, ct);
        foreach (var row in rows) { row.AttemptId = null; row.LeaseUntilUtc = null; foreach (var file in row.Files) file.StorageKey = ""; }
        return rows;
    }
}
