namespace TenantPlatform.Application.Security;

// Supplied by a trusted host adapter, never by a resource/filter argument.
public sealed record ActorSnapshot(bool IsAuthenticated, Guid UserId, Guid? AccountId);

public interface ICurrentActor
{
    ActorSnapshot GetSnapshot();
}
