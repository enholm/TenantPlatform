using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agent;

// Temporary metadata and cancellation only. Never stores questions, answers, tool data or scoped services.
// One-instance pilot: session ownership/concurrency require affinity; not a distributed quota.
public sealed class ReadAgentSessions(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Session> sessions = [];
    private sealed class Session(ActorSnapshot owner, Guid connection, DateTimeOffset end)
    {
        public ActorSnapshot Owner { get; } = owner;
        public Guid Connection { get; } = connection;
        public DateTimeOffset End { get; } = end;
        public CancellationTokenSource? Run { get; set; }
    }
    public Guid Create(ActorSnapshot owner, Guid connection)
    {
        lock (gate)
        {
            Prune();
            // Browser New/Cancel explicitly destroys its previous conversation. Retain at most four
            // metadata-only sessions per user so the configured per-user concurrency is meaningful.
            var owned = sessions.Where(p => p.Value.Owner.UserId == owner.UserId).ToArray();
            if (owned.Length >= 4)
            {
                var oldestIdle = owned.Where(p => p.Value.Run is null).OrderBy(p => p.Value.End).FirstOrDefault();
                if (oldestIdle.Value is null) throw new ReadAgentException("Busy");
                sessions.Remove(oldestIdle.Key);
            }
            if (sessions.Count >= 1000) throw new ReadAgentException("Busy");
            var id = Guid.NewGuid(); sessions[id] = new(owner, connection, clock.GetUtcNow().AddMinutes(30)); return id;
        }
    }
    public Guid Connection(Guid id, ActorSnapshot owner)
    {
        lock (gate) return Require(id, owner).Connection;
    }
    public Lease Start(Guid id, ActorSnapshot owner, ReadAgentLimits limits, CancellationToken ct)
    {
        lock (gate)
        {
            var session = Require(id, owner);
            if (session.Run is not null || sessions.Values.Count(s => s.Owner.UserId == owner.UserId && s.Run is not null) >= limits.ConcurrentRunsPerUser)
                throw new ReadAgentException("Busy");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(limits.Seconds)); session.Run = cts;
            return new(cts, () => { lock (gate) { if (session.Run == cts) session.Run = null; } });
        }
    }
    public void Verify(Guid id, ActorSnapshot owner) { lock (gate) Require(id, owner); }
    public void Cancel(Guid id, ActorSnapshot owner)
    {
        lock (gate) { var session = Require(id, owner); session.Run?.Cancel(); sessions.Remove(id); }
    }
    public void ClearUser(Guid user) { lock (gate) ClearUserCore(user); }
    private void ClearUserCore(Guid user)
    {
        foreach (var pair in sessions.Where(p => p.Value.Owner.UserId == user).ToArray()) { pair.Value.Run?.Cancel(); sessions.Remove(pair.Key); }
    }
    private void Prune()
    {
        foreach (var pair in sessions.Where(p => p.Value.End <= clock.GetUtcNow()).ToArray()) { pair.Value.Run?.Cancel(); sessions.Remove(pair.Key); }
    }
    private Session Require(Guid id, ActorSnapshot owner)
    {
        Prune();
        if (!owner.IsAuthenticated || owner.AccountId is null || !sessions.TryGetValue(id, out var value) || value.Owner != owner)
            throw new UnauthorizedAccessException();
        return value;
    }
    public sealed class Lease(CancellationTokenSource source, Action release) : IDisposable
    {
        public CancellationToken Token => source.Token;
        public void Dispose() { release(); source.Dispose(); }
    }
}
