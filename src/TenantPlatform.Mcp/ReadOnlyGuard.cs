using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenantPlatform.Application.Security;

namespace TenantPlatform.Mcp;

public sealed class ReadOnlyGuard : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => throw new InvalidOperationException("MCP is read only.");
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("MCP is read only.");
}

// This holder exists only inside a NEW scope created for one tool call. Bind before resolving Application.
// Nothing from an HTTP/session scope can mutate it after binding.
internal sealed class OperationActor : ICurrentActor
{
    private ActorSnapshot? snapshot;
    public void Bind(Guid user, Guid? account)
    {
        if (snapshot is not null) throw new InvalidOperationException("Actor is already frozen.");
        snapshot = new(true, user, account);
    }
    public ActorSnapshot GetSnapshot() => snapshot ?? throw new InvalidOperationException("Missing operation actor.");
}
