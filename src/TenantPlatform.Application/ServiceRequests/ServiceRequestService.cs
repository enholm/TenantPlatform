using System.Text;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;

public sealed class ServiceRequestService(IAuthorizationService authorization, IServiceRequestData data,
    IServiceRequestEmailAddressService addresses, TimeProvider clock) : IServiceRequestService, IServiceRequestEmailComposer
{
    private enum Action { Approve, Complete, Start, Assign, Comment, Reply, Queue }
    private async Task<AccountActorScope> Actor(Guid account, CancellationToken ct) => await authorization.GetAccountActorScopeAsync(account, ct);
    private static bool Allowed(AccountActorScope actor, ServiceRequest request, Action action) => action switch
    {
        Action.Approve or Action.Assign or Action.Queue => ServiceRequestAccess.Admin(actor, request),
        Action.Complete => ServiceRequestAccess.Admin(actor, request) || ServiceRequestAccess.Provider(actor, request),
        Action.Start => ServiceRequestAccess.Provider(actor, request),
        Action.Comment => ServiceRequestAccess.Read(actor, request),
        Action.Reply => ServiceRequestAccess.Reply(actor, request),
        _ => false
    };
    private async Task Mutate(Guid account, Guid id, Action action, System.Action<AccountActorScope, ServiceRequestState> change, CancellationToken ct)
    {
        var initial = await Actor(account, ct);
        var before = await data.ReadRequestAsync(account, id, ct);
        if (before is null || !Allowed(initial, before, action)) throw new UnauthorizedAccessException();
        bool queue;
        await using (var tx = await data.BeginRequestAsync(account, id, ct))
        {
            var actor = await Actor(account, ct); var request = tx.State.Request ?? throw new UnauthorizedAccessException();
            if (actor.UserId != initial.UserId || !Allowed(actor, request, action)) throw new UnauthorizedAccessException();
            change(actor, tx.State);
            await tx.CommitAsync(request, false, ct);
            queue = action is Action.Assign or Action.Approve && request.AssignedServiceProviderOrganizationId.HasValue;
        }
        // Preserve the existing assignment/approval commit before separate mail ordering.
        if (queue) await QueueProviderEmailAsync(account, id, ct);
    }
    private void History(ServiceRequestState state, Guid user, ServiceRequestEventType type, string body) => state.Messages.Add(new()
    { Id = Guid.NewGuid(), ServiceRequestId = state.Request!.Id, Direction = ServiceRequestMessageDirection.Internal,
        Type = ServiceRequestMessageType.System, EventType = type, CreatedByUserId = user, Body = body, CreatedAt = clock.GetUtcNow() });
    public async Task<Guid> CreateServiceRequestAsync(Guid accountId, CreateServiceRequestRequest request, CancellationToken cancellationToken = default)
    {
        var definition = request.ServiceDefinitionId; var occupancyId = request.OccupancyId;
        var values = request.Values.Select(x => new CreateServiceRequestFieldValueRequest { ServiceDefinitionFieldId = x.ServiceDefinitionFieldId, Value = x.Value }).ToList();
        var actor = await Actor(accountId, cancellationToken);
        await using var tx = await data.BeginCreateAsync(accountId, definition, occupancyId, cancellationToken);
        var current = await Actor(accountId, cancellationToken);
        if (current.UserId != actor.UserId) throw new UnauthorizedAccessException();
        var state = tx.State;
        if (state.Definition is not { IsActive: true, IsBookableByTenant: true }) throw new ServiceRequestValidationException("ServiceNotAvailable");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (state.Occupancy is not { } occupancy || state.Unit is not { } unit || occupancy.ValidFrom > today || occupancy.ValidTo < today)
            throw new ServiceRequestValidationException("ServiceRequestInvalidOccupancy");
        var now = clock.GetUtcNow();
        var entity = new ServiceRequest { Id = Guid.NewGuid(), AccountId = accountId, ServiceDefinitionId = definition, RequesterUserId = actor.UserId,
            RequesterOrganizationId = occupancy.TenantOrganizationId, BuildingId = unit.BuildingId, UnitId = unit.Id, Status = ServiceRequestStatus.Submitted,
            CreatedAt = now, SubmittedAt = now, ReplyToken = ServiceRequestRules.CreateReplyToken() };
        if (!ServiceRequestAccess.Admin(current, entity) && !ServiceRequestAccess.Tenant(current, entity)) throw new UnauthorizedAccessException();
        foreach (var value in ServiceRequestRules.ValidateAndNormalizeValues(state.Fields, values))
            state.Values.Add(new() { Id = Guid.NewGuid(), ServiceRequestId = entity.Id, ServiceDefinitionFieldId = value.ServiceDefinitionFieldId, Value = value.Value });
        state.Messages.Add(new() { Id = Guid.NewGuid(), ServiceRequestId = entity.Id, Direction = ServiceRequestMessageDirection.Internal, Type = ServiceRequestMessageType.System,
            EventType = ServiceRequestEventType.Submitted, CreatedByUserId = actor.UserId, Body = "Service request submitted.", CreatedAt = now });
        await tx.CommitAsync(entity, true, cancellationToken); return entity.Id;
    }
    public async Task<List<ServiceRequestListItemDto>> GetMyRequestsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) =>
        await data.ReadListAsync(ServiceRequestAccess.ReadScope(await Actor(accountId, cancellationToken)), ServiceRequestListKind.Mine, languageCode, cancellationToken);
    public async Task<List<ServiceRequestListItemDto>> GetAssignedRequestsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) =>
        await data.ReadListAsync(ServiceRequestAccess.ReadScope(await Actor(accountId, cancellationToken)), ServiceRequestListKind.Assigned, languageCode, cancellationToken);
    public async Task<ServiceRequestDetailsDto?> GetRequestAsync(Guid accountId, Guid requestId, string languageCode, CancellationToken cancellationToken = default) =>
        await data.ReadDetailsAsync(ServiceRequestAccess.ReadScope(await Actor(accountId, cancellationToken)), requestId, languageCode, cancellationToken);
    public async Task<ServiceRequestAdminListDto> GetAdminRequestsAsync(Guid accountId, string languageCode, ServiceRequestAdminFilter filter, CancellationToken cancellationToken = default)
    {
        var status = filter.Status; var definition = filter.ServiceDefinitionId; var building = filter.BuildingId;
        var provider = filter.AssignedProviderOrganizationId; var search = filter.Search?.Trim();
        var all = await data.ReadAdminAsync(ServiceRequestAccess.ReadScope(await Actor(accountId, cancellationToken)), languageCode, cancellationToken);
        bool Contains(string? text) => text?.Contains(search!, StringComparison.CurrentCultureIgnoreCase) == true;
        return new() { Requests = all.Where(x => (!status.HasValue || x.Status == status) && (!definition.HasValue || x.ServiceDefinitionId == definition) &&
            (!building.HasValue || x.BuildingId == building) && (!provider.HasValue || x.AssignedProviderOrganizationId == provider) &&
            (string.IsNullOrWhiteSpace(search) || Contains(x.Title) || Contains(x.RequesterName) || Contains(x.RequesterEmail) || Contains(x.RequesterOrganizationName) || Contains(x.ServiceName))).ToList(),
            Services = all.Select(x => new ServiceRequestAdminFilterOptionDto(x.ServiceDefinitionId, x.ServiceName)).DistinctBy(x => x.Id).OrderBy(x => x.Name).ToList(),
            Buildings = all.Select(x => new ServiceRequestAdminFilterOptionDto(x.BuildingId, x.BuildingName)).DistinctBy(x => x.Id).OrderBy(x => x.Name).ToList(),
            Providers = all.Where(x => x.AssignedProviderOrganizationId.HasValue).Select(x => new ServiceRequestAdminFilterOptionDto(x.AssignedProviderOrganizationId!.Value, x.AssignedProviderOrganizationName ?? "")).DistinctBy(x => x.Id).OrderBy(x => x.Name).ToList() };
    }
    public Task ApproveRequestAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => Mutate(accountId, requestId, Action.Approve, (actor, state) =>
    {
        var request = state.Request!;
        if (request.Status != ServiceRequestStatus.Submitted) throw new ServiceRequestValidationException("ServiceRequestCannotBeApproved");
        request.Status = ServiceRequestStatus.Approved;
        var provider = state.Providers.SingleOrDefault(x => x.IsActive && x.IsDefault);
        if (provider is not null) { request.AssignedServiceProviderOrganizationId = provider.ServiceProviderOrganizationId; History(state, actor.UserId, ServiceRequestEventType.Assigned, "Service request assigned to provider."); }
        History(state, actor.UserId, ServiceRequestEventType.Approved, "Service request approved.");
    }, cancellationToken);
    public Task CompleteRequestAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => Mutate(accountId, requestId, Action.Complete, (actor, state) =>
    {
        var request = state.Request!;
        var admin = ServiceRequestAccess.Admin(actor, request);
        if (request.Status != ServiceRequestStatus.InProgress && !(admin && request.Status == ServiceRequestStatus.Approved)) throw new ServiceRequestValidationException("ServiceRequestCannotBeCompleted");
        request.Status = ServiceRequestStatus.Completed; request.CompletedAt = clock.GetUtcNow(); History(state, actor.UserId, ServiceRequestEventType.Completed, "Service request completed.");
    }, cancellationToken);
    public Task StartWorkAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => Mutate(accountId, requestId, Action.Start, (actor, state) =>
    {
        if (state.Request!.Status != ServiceRequestStatus.Approved) throw new ServiceRequestValidationException("ServiceRequestCannotStart");
        state.Request.Status = ServiceRequestStatus.InProgress; History(state, actor.UserId, ServiceRequestEventType.InProgress, "Work started.");
    }, cancellationToken);
    public Task AssignProviderAsync(Guid accountId, Guid serviceRequestId, Guid providerOrganizationId, CancellationToken cancellationToken = default) => Mutate(accountId, serviceRequestId, Action.Assign, (actor, state) =>
    {
        var request = state.Request!;
        if (request.Status != ServiceRequestStatus.Approved) throw new ServiceRequestValidationException("ServiceRequestProviderAssignmentNotAllowed");
        if (request.AssignedServiceProviderOrganizationId.HasValue) throw new ServiceRequestValidationException("ServiceRequestAlreadyAssigned");
        if (!state.Providers.Any(x => x.IsActive && x.ServiceProviderOrganizationId == providerOrganizationId)) throw new ServiceRequestValidationException("ServiceRequestProviderNotAvailable");
        request.AssignedServiceProviderOrganizationId = providerOrganizationId; History(state, actor.UserId, ServiceRequestEventType.Assigned, "Service request assigned to provider.");
    }, cancellationToken);
    public async Task<IReadOnlyList<ServiceRequestProviderOptionDto>> GetAvailableProvidersAsync(Guid accountId, Guid serviceRequestId, CancellationToken cancellationToken = default)
    {
        var actor = await Actor(accountId, cancellationToken); var request = await data.ReadRequestAsync(accountId, serviceRequestId, cancellationToken);
        if (request is null || !ServiceRequestAccess.Admin(actor, request)) throw new UnauthorizedAccessException();
        return await data.ReadProviderOptionsAsync(accountId, request.ServiceDefinitionId, cancellationToken);
    }
    public Task AddCommentAsync(Guid accountId, Guid requestId, string comment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment)) throw new ServiceRequestValidationException("CommentRequired");
        var text = comment.Trim(); if (text.Length > 4000) throw new ServiceRequestValidationException("CommentTooLong");
        return Mutate(accountId, requestId, Action.Comment, (actor, state) => state.Messages.Add(new() { Id = Guid.NewGuid(), ServiceRequestId = requestId,
            Type = ServiceRequestMessageType.Comment, Direction = ServiceRequestMessageDirection.Internal, CreatedByUserId = actor.UserId, Body = text, CreatedAt = clock.GetUtcNow() }), cancellationToken);
    }
    public Task ReplyToEmailAsync(Guid accountId, Guid requestId, Guid messageId, string reply, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reply)) throw new ServiceRequestValidationException("ReplyRequired");
        var text = reply.Trim(); if (text.Length > 4000) throw new ServiceRequestValidationException("ReplyTooLong");
        return Mutate(accountId, requestId, Action.Reply, (_, state) =>
        {
            var request = state.Request!;
            if (ServiceRequestRules.IsClosedStatus(request.Status)) throw new ServiceRequestValidationException("ServiceRequestReplyClosed");
            var latest = state.Messages.Where(x => x.Type == ServiceRequestMessageType.Email && x.Direction == ServiceRequestMessageDirection.Inbound)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault() ?? throw new ServiceRequestValidationException("ServiceRequestEmailNotFound");
            if (latest.Id != messageId) throw new ServiceRequestValidationException("ServiceRequestCanOnlyReplyToLatestEmail");
            if (string.IsNullOrWhiteSpace(latest.FromAddress)) throw new ServiceRequestValidationException("ServiceRequestEmailNotFound");
            var now = clock.GetUtcNow(); state.Outbox.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, ServiceRequestId = requestId, ToAddress = latest.FromAddress,
                ReplyToAddress = addresses.GetReplyAddress(request.ReplyToken), Subject = ServiceRequestRules.CreateReplySubject(latest.Subject),
                Body = ServiceRequestRules.CreateReplyBody(text, latest), InReplyToMessageId = latest.ExternalMessageId,
                References = ServiceRequestRules.BuildReferences(latest.References, latest.ExternalMessageId), Status = EmailOutboxStatus.Pending, CreatedAt = now, NextAttemptAt = now });
        }, cancellationToken);
    }
    public async Task<ComposedEmail> ComposeProviderRequestAsync(Guid accountId, Guid serviceRequestId, CancellationToken cancellationToken = default)
    {
        var actor = await Actor(accountId, cancellationToken); var request = await data.ReadRequestAsync(accountId, serviceRequestId, cancellationToken);
        if (request is null || !ServiceRequestAccess.Admin(actor, request)) throw new UnauthorizedAccessException();
        var detail = await data.ReadEmailDetailsAsync(ServiceRequestAccess.ReadScope(actor), serviceRequestId, cancellationToken) ?? throw new UnauthorizedAccessException();
        var body = new StringBuilder().AppendLine("A new service request has been assigned to you.").AppendLine().AppendLine($"Service: {detail.ServiceName}")
            .AppendLine($"Organisation: {detail.RequesterOrganizationName}").AppendLine($"Building: {detail.BuildingName}");
        if (!string.IsNullOrWhiteSpace(detail.UnitName)) body.AppendLine($"Unit: {detail.UnitName}");
        body.AppendLine().AppendLine("Request details:").AppendLine();
        foreach (var value in detail.Values) body.AppendLine($"{value.Label}: {value.Value}");
        body.AppendLine().AppendLine("You can reply directly to this email. Your reply will be added to the service request.");
        return new ComposedEmail { Subject = $"Service request: {detail.ServiceName}", Body = body.ToString() };
    }
    public async Task QueueProviderEmailAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var initial = await Actor(accountId, cancellationToken);
        var before = await data.ReadRequestAsync(accountId, requestId, cancellationToken);
        if (before is null || !Allowed(initial, before, Action.Queue)) throw new UnauthorizedAccessException();
        await using var tx = await data.BeginRequestAsync(accountId, requestId, cancellationToken);
        var actor = await Actor(accountId, cancellationToken); var request = tx.State.Request ?? throw new UnauthorizedAccessException();
        if (actor.UserId != initial.UserId || !Allowed(actor, request, Action.Queue)) throw new UnauthorizedAccessException();
        var provider = tx.State.Providers.FirstOrDefault(x => x.IsActive && x.ServiceProviderOrganizationId == request.AssignedServiceProviderOrganizationId);
        if (provider is not { IntegrationType: ServiceProviderIntegrationType.Email } || string.IsNullOrWhiteSpace(provider.RequestEmailAddress) || tx.State.Outbox.Any(x => x.Status != EmailOutboxStatus.Failed)) return;
        var email = await ComposeProviderRequestAsync(accountId, requestId, cancellationToken); var now = clock.GetUtcNow();
        tx.State.Outbox.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, ServiceRequestId = requestId, ToAddress = provider.RequestEmailAddress,
            ReplyToAddress = addresses.GetReplyAddress(request.ReplyToken), Subject = email.Subject, Body = email.Body, Status = EmailOutboxStatus.Pending, CreatedAt = now, NextAttemptAt = now });
        await tx.CommitAsync(request, false, cancellationToken);
    }
}
