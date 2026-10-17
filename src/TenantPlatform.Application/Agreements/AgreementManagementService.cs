using System.Text.Json;
using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed class AgreementManagementService(IAuthorizationService authorization, IAgreementManagementData data, TimeProvider clock)
{
    private async Task<Guid> Admin(Guid account, CancellationToken ct)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!actor.Assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Agreements.Create) && !x.OrganizationId.HasValue && !x.BuildingId.HasValue)) throw new UnauthorizedAccessException();
        return actor.UserId;
    }
    private async Task<AgreementReadScope> Require(Guid account, Guid agreement, bool manage, CancellationToken ct)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        if (manage) { if (!scope.CanManageAccess) throw new UnauthorizedAccessException(); }
        else
        {
            if (scope.IsArchived && scope.CanEditBeforeArchive) throw new AgreementValidationException("FollowupArchived");
            if (!scope.CanEdit) throw new UnauthorizedAccessException();
        }
        return scope;
    }
    private static void Revision(Agreement a, Guid revision) { if (a.Revision != revision) throw new AgreementValidationException("AgreementConcurrencyConflict"); }
    private void Touch(Agreement a, Guid user) { a.Revision = Guid.NewGuid(); a.UpdatedUtc = clock.GetUtcNow(); a.UpdatedByUserId = user; }
    private void Notice(AgreementManagementState state, Agreement a, AgreementNoticeSnapshot before, AgreementNoticeAction action)
    {
        var after = AgreementNoticeSnapshot.From(a); if (before == after && action == AgreementNoticeAction.RuleChanged) return;
        state.NoticeHistory.Add(new() { Id = Guid.NewGuid(), AccountId = a.AccountId, AgreementId = a.Id, Action = action, CreatedUtc = clock.GetUtcNow(), ActorUserId = a.UpdatedByUserId,
            BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after) });
    }
    private void Selection(AgreementManagementState state, Agreement a, Guid? index, int sequence, DateOnly date, Guid user) => state.Selections.Add(new() { Id = Guid.NewGuid(), AccountId = a.AccountId, AgreementId = a.Id, IndexId = index, Sequence = sequence, EffectiveFrom = date, RecordedUtc = clock.GetUtcNow(), ActorUserId = user });
    public async Task<AgreementOptionsDto> GetOptionsAsync(Guid accountId, Guid? agreementId = null, CancellationToken cancellationToken = default)
    {
        Agreement? current = null;
        if (agreementId.HasValue) { await Require(accountId, agreementId.Value, false, cancellationToken); current = await data.ReadAgreementAsync(accountId, agreementId.Value, cancellationToken); }
        else await Admin(accountId, cancellationToken);
        var state = await data.ReadReferencesAsync(accountId, cancellationToken); var options = state.Options;
        return new() { OrganizationElements = AccountRegisterHierarchy.Build(state.Elements.Select(x => (x.Id, x.ParentId, x.Name))).Where(x => state.Elements.Single(e => e.Id == x.Id).IsActive || current?.OrganizationElementId == x.Id).Select(x => new AgreementOptionDto(x.Id, x.Path, IsActive: state.Elements.Single(e => e.Id == x.Id).IsActive)).ToList(),
            GeographicAreas = AccountRegisterHierarchy.Build(state.Areas.Select(x => (x.Id, x.ParentId, x.Name))).Where(x => state.Areas.Single(e => e.Id == x.Id).IsActive || current?.GeographicAreaId == x.Id).Select(x => new AgreementOptionDto(x.Id, x.Path, IsActive: state.Areas.Single(e => e.Id == x.Id).IsActive)).ToList(),
            Indices = options.Indices, Counterparties = options.Counterparties, Members = options.Members, Buildings = options.Buildings, Units = options.Units };
    }
    private static SaveAgreementRequest Snapshot(SaveAgreementRequest request) => JsonSerializer.Deserialize<SaveAgreementRequest>(JsonSerializer.Serialize<SaveAgreementRequest>(request))!;
    public async Task<Guid> CreateAsync(Guid accountId, SaveAgreementRequest request, CancellationToken cancellationToken = default)
    {
        request = Snapshot(request); var user = await Admin(accountId, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, null, cancellationToken);
        if (await Admin(accountId, cancellationToken) != user) throw new UnauthorizedAccessException();
        var a = PrepareCreate(tx.State, accountId, request, user);
        await tx.SynchronizeAsync(clock.GetUtcNow(), user, cancellationToken); await tx.CommitAsync(cancellationToken); return a.Id;
    }
    internal Agreement PrepareCreate(AgreementManagementState state, Guid accountId, SaveAgreementRequest request, Guid user)
    {
        Validate(state.References, request, null);
        var a = new Agreement { Id = Guid.NewGuid(), AccountId = accountId, CreatedUtc = clock.GetUtcNow(), CreatedByUserId = user };
        if (request.Form == AgreementForm.Legacy || request.BeginNewPeriod) throw new AgreementValidationException("NoticeChooseForm");
        request.Direction ??= request.Type == AgreementType.Supplier ? AgreementDirection.Cost : null;
        a.CurrentPeriodStartDate = request.StartDate; Apply(a, request); Touch(a, user); state.Agreement = a;
        Notice(state, a, new(), AgreementNoticeAction.RuleChanged);
        if (a.IndexId.HasValue) Selection(state, a, a.IndexId, 1, a.StartDate, user);
        return a;
    }
    public async Task UpdateAsync(Guid accountId, Guid agreementId, SaveAgreementRequest request, CancellationToken cancellationToken = default)
    {
        request = Snapshot(request); var initial = await Require(accountId, agreementId, false, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, agreementId, cancellationToken);
        var scope = await Require(accountId, agreementId, false, cancellationToken); if (scope.ListScope.UserId != initial.ListScope.UserId) throw new UnauthorizedAccessException();
        var user = scope.ListScope.UserId; var state = tx.State; var a = state.Agreement ?? throw new UnauthorizedAccessException(); Revision(a, request.Revision);
        if (!scope.CanManageAccess && request.OwnerUserId != a.OwnerUserId) throw new UnauthorizedAccessException();
        Validate(state.References, request, a);
        if (request.Form == AgreementForm.Legacy && a.Form != AgreementForm.Legacy) throw new AgreementValidationException("NoticeChooseForm");
        if ((a.Direction != request.Direction || a.Currency != request.Currency || a.CounterpartyOrganizationId != request.CounterpartyOrganizationId) && state.HasActivatedLines) throw new AgreementValidationException("FinanceAgreementLocked");
        var before = AgreementNoticeSnapshot.From(a);
        if (request.BeginNewPeriod)
        {
            if (a.Form != AgreementForm.Renewing || request.Form != AgreementForm.Renewing || !request.NewPeriodStartDate.HasValue || request.NewPeriodStartDate <= a.CurrentPeriodStartDate || !request.RenewalDate.HasValue || request.RenewalDate <= request.NewPeriodStartDate) throw new AgreementValidationException("NoticeInvalidPeriod");
            a.CurrentPeriodStartDate = request.NewPeriodStartDate.Value;
        }
        if (!request.IndexId.HasValue && (!a.IndexSetupNeedsReview || request.ResolveIndexSetup) && state.LineVersions.GroupBy(x => x.LineId).Any(g => g.MaxBy(x => x.Sequence)!.IndexRegulated)) throw new AgreementValidationException("SimpleIndexRequired");
        if (a.IndexId != request.IndexId || request.ResolveIndexSetup) foreach (var proposal in state.Proposals) proposal.Status = AgreementProposalStatus.Stale;
        if (a.IndexId != request.IndexId)
        {
            var last = state.Selections.OrderByDescending(x => x.Sequence).FirstOrDefault(); var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            Selection(state, a, request.IndexId, (last?.Sequence ?? 0) + 1, last is null ? a.StartDate : today > a.StartDate ? today : a.StartDate, user);
        }
        var previous = a.OwnerUserId; Apply(a, request); Touch(a, user); Notice(state, a, before, request.BeginNewPeriod ? AgreementNoticeAction.PeriodStarted : AgreementNoticeAction.RuleChanged);
        await tx.SynchronizeAsync(clock.GetUtcNow(), user, cancellationToken);
        if (previous != a.OwnerUserId)
        foreach (var deadline in state.Deadlines.Where(x => x.State == AgreementDeadlineState.Current && x.AssignedUserId == null))
        {
            deadline.UpdatedUtc = clock.GetUtcNow(); deadline.UpdatedByUserId = user;
            state.History.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, DeadlineId = deadline.Id, Kind = AgreementHistoryKind.Assigned, CreatedUtc = clock.GetUtcNow(), ActorUserId = user, AssignedUserId = a.OwnerUserId });
        }
        await tx.CommitAsync(cancellationToken);
    }
    public async Task SetAccessAsync(Guid accountId, Guid agreementId, Guid userId, AgreementAccessLevel? level, Guid revision, CancellationToken cancellationToken = default)
    {
        var initial = await Require(accountId, agreementId, true, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, agreementId, cancellationToken);
        var scope = await Require(accountId, agreementId, true, cancellationToken); if (scope.ListScope.UserId != initial.ListScope.UserId) throw new UnauthorizedAccessException();
        var state = tx.State; var a = state.Agreement ?? throw new UnauthorizedAccessException(); Revision(a, revision);
        if (level.HasValue && (!Enum.IsDefined(level.Value) || !state.References.Options.Members.Any(x => x.Id == userId))) throw new AgreementValidationException("AgreementInvalidMember");
        var grant = state.Grants.SingleOrDefault(x => x.UserId == userId);
        if (level is null) { if (grant is not null) state.Grants.Remove(grant); }
        else if (grant is null) state.Grants.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, UserId = userId, Level = level.Value });
        else grant.Level = level.Value;
        Touch(a, scope.ListScope.UserId); await tx.CommitAsync(cancellationToken);
    }
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
    private static void Validate(AgreementReferenceState state, SaveAgreementRequest r, Agreement? existing)
    {
        var options = state.Options;
        if (r.OrganizationElementId.HasValue && !state.Elements.Any(x => x.Id == r.OrganizationElementId && (x.IsActive || x.Id == existing?.OrganizationElementId))) throw new AgreementValidationException("AgreementInvalidOrganizationElement");
        if (r.GeographicAreaId.HasValue && !state.Areas.Any(x => x.Id == r.GeographicAreaId && (x.IsActive || x.Id == existing?.GeographicAreaId))) throw new AgreementValidationException("AgreementInvalidGeographicArea");
        if (r.IndexId.HasValue && !options.Indices.Any(x => x.Id == r.IndexId)) throw new AgreementValidationException("FinanceInvalidReference");
        if ((r.Direction.HasValue && !Enum.IsDefined(r.Direction.Value)) || (!string.IsNullOrEmpty(r.Currency) && !AgreementPeriodCalculator.Currencies.Contains(r.Currency))) throw new AgreementValidationException("FinanceInvalidSettings");
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 200 || r.Description?.Length > 4000 || r.Terms?.Length > 10000 || !Enum.IsDefined(r.Type) || !Enum.IsDefined(r.Status)) throw new AgreementValidationException("AgreementInvalidDetails");
        if (r.StartDate == default || r.Form != AgreementForm.Ongoing && r.EndDate < r.StartDate) throw new AgreementValidationException("AgreementInvalidDates");
        if ((r.Form is AgreementForm.Legacy or AgreementForm.Renewing) && r.RenewalMonths.HasValue && (!r.AutoRenew || r.RenewalMonths <= 0)) throw new AgreementValidationException("AgreementInvalidRenewal");
        if (!options.Counterparties.Any(x => x.Id == r.CounterpartyOrganizationId)) throw new AgreementValidationException("AgreementInvalidCounterparty");
        if (!options.Members.Any(x => x.Id == r.OwnerUserId)) throw new AgreementValidationException("AgreementInvalidMember");
        if (r.BuildingId.HasValue && !options.Buildings.Any(x => x.Id == r.BuildingId)) throw new AgreementValidationException("AgreementInvalidBuilding");
        if (r.UnitId.HasValue && (!r.BuildingId.HasValue || !options.Units.Any(x => x.Id == r.UnitId && x.BuildingId == r.BuildingId))) throw new AgreementValidationException("AgreementInvalidUnit");
    }
}
