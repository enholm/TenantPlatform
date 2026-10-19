using System.Collections.Concurrent;
using TenantPlatform.Application.Security;
namespace TenantPlatform.Application.PersonalPages;

// One-instance pilot. Each active response has a deadline and is cancelled by identity changes.
public sealed class PersonalPageRuns
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();
    public IDisposable Start(ActorSnapshot who, CancellationToken ct, out CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct); source.CancelAfter(TimeSpan.FromSeconds(40));
        lock (gate) { if (active.Count >= 200 || !active.TryAdd(who.UserId, source)) { source.Dispose(); throw new PersonalPageException("Busy"); } }
        token = source.Token; return new Lease(() => { active.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(who.UserId, source)); source.Dispose(); });
    }
    public void CancelUser(Guid user) { if (active.TryGetValue(user, out var source)) { try { source.Cancel(); } catch (ObjectDisposedException) { } } }
    sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}
