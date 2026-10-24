using System.Text;
using System.Text.Json;
using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Agent;

// Bounded, process-local completed turns. No credentials, provider state or scoped services.
public sealed class ReadAgentSessions : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly TimeProvider clock;
    private readonly ITimer expiry;
    public ReadAgentSessions(TimeProvider clock)
    {
        this.clock = clock;
        expiry = clock.CreateTimer(_ => { lock (gate) Prune(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    public void Dispose() { expiry.Dispose(); lock (gate) { foreach (var s in sessions.Values) s.Run?.Cancel(); sessions.Clear(); } }
    private readonly object gate = new();
    private long sequence;
    private readonly Dictionary<Guid, Session> sessions = [];
    private readonly Dictionary<CancellationTokenSource, (Guid User, long Bytes, long DraftBytes)> running = [];
    private sealed class Session(ActorSnapshot owner, Guid connection, DateTimeOffset end, long order)
    {
        public ActorSnapshot Owner { get; } = owner;
        public Guid Connection { get; } = connection;
        public DateTimeOffset End { get; } = end;
        public long Order { get; } = order;
        public CancellationTokenSource? Run { get; set; }
        public List<AgentTurn> History { get; } = [];
        public AgentReportDraft? Draft { get; set; }
        public bool Invalidated { get; set; }
        public bool Trimmed { get; set; }
        public long Bytes { get; set; }
    }
    public Guid Create(ActorSnapshot owner, Guid connection)
    {
        lock (gate)
        {
            Prune();
            var owned = sessions.Where(p => p.Value.Owner.UserId == owner.UserId).ToArray();
            if (owned.Length >= 4)
            {
                var idle = owned.Where(p => p.Value.Run is null).OrderBy(p => p.Value.End).ThenBy(p => p.Value.Order).FirstOrDefault();
                if (idle.Value is null) throw new ReadAgentException("Busy");
                sessions.Remove(idle.Key);
            }
            if (sessions.Count >= 1000) throw new ReadAgentException("Busy");
            var id = Guid.NewGuid(); sessions[id] = new(owner, connection, clock.GetUtcNow().Add(Lifetime), ++sequence); return id;
        }
    }
    public Guid Connection(Guid id, ActorSnapshot owner) { lock (gate) return Require(id, owner).Connection; }
    public Lease Start(Guid id, ActorSnapshot owner, ReadAgentLimits limits, CancellationToken ct)
    {
        lock (gate)
        {
            var session = Require(id, owner);
            if (session.Run is not null || running.Count >= 1000 || running.Values.Count(s => s.User == owner.UserId) >= limits.ConcurrentRunsPerUser)
                throw new ReadAgentException("Busy");
            Trim(session, limits);
            // Reserve the lease snapshot too: cancellation/deletion must not free its accounting or quota early.
            while (TotalBytes() + session.Bytes > limits.ServerHistoryBytes)
            {
                var idle = sessions.Values.Where(s => s != session && s.Run is null && s.History.Count > 0).OrderBy(s => s.End).ThenBy(s => s.Order).FirstOrDefault();
                var target = idle ?? session;
                if (target.History.Count == 0) throw new ReadAgentException("Busy");
                target.Bytes -= Size(target.History[0]); target.History.RemoveAt(0); target.Trimmed = true;
            }
            if (DraftBytes() + SizeDraft(session.Draft) > 4 * 1024 * 1024) throw new ReadAgentException("DraftLimit");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, Math.Min(limits.Seconds, (session.End - clock.GetUtcNow()).TotalSeconds)))); session.Run = cts;
            running.Add(cts, (owner.UserId, session.Bytes, SizeDraft(session.Draft)));
            var committed = false;
            return new(cts, session.History.ToArray(), session.Trimmed, session.Invalidated, session.Draft,
                (turn, draft) => {
                    lock (gate)
                    {
                        if (!sessions.TryGetValue(id, out var current) || current != session || session.Run != cts || committed || cts.IsCancellationRequested || session.End <= clock.GetUtcNow())
                            throw new ReadAgentException("CancelledOrTimedOut");
                        if (draft is not null) ValidateDraft(draft);
                        session.History.Add(turn); Trim(session, limits);
                        // Deterministic oldest idle session/oldest whole turn. Never evict active conversations.
                        while (TotalBytes() > limits.ServerHistoryBytes)
                        {
                            var candidate = sessions.Values.Where(s => s != session && s.Run is null && s.History.Count > 0).OrderBy(s => s.End).ThenBy(s => s.Order).FirstOrDefault();
                            if (candidate is null) { session.History.Remove(turn); session.Bytes = session.History.Sum(Size); session.Trimmed = true; break; }
                            candidate.Bytes -= Size(candidate.History[0]); candidate.History.RemoveAt(0); candidate.Trimmed = true;
                        }
                        if (cts.IsCancellationRequested)
                        {
                            session.History.Remove(turn); session.Bytes = session.History.Sum(Size);
                            throw new ReadAgentException("CancelledOrTimedOut");
                        }
                        if (draft is not null) session.Draft = draft;
                        committed = true;
                        var trimmed = session.Trimmed; session.Trimmed = false; session.Invalidated = false; return (trimmed, session.History.Contains(turn), session.History.Count);
                    }
                }, () => { lock (gate) { if (session.Run == cts) { var questions = session.History.Select(t => new AgentTurn(t.Question, null, [])).ToArray(); session.History.Clear(); session.History.AddRange(questions); session.Invalidated = true; session.Bytes = session.History.Sum(Size); session.Trimmed = false; } } },
                () => { lock (gate) { if (session.Run == cts) session.Draft = null; } },
                draft => { lock (gate) {
                    if (!sessions.TryGetValue(id, out var current) || current != session || session.Run != cts || committed || cts.IsCancellationRequested || session.End <= clock.GetUtcNow()) throw new ReadAgentException("CancelledOrTimedOut");
                    ValidateDraft(draft); session.Draft = draft; committed = true;
                } },
                () => { lock (gate) { if (session.Run == cts) session.Run = null; running.Remove(cts); } });
            void ValidateDraft(AgentReportDraft draft)
            {
                var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(draft));
                if (draft.Dependencies?.Count > 2 || bytes > 32768 || DraftBytes() - SizeDraft(session.Draft) + bytes * 4L + 1024 > 4 * 1024 * 1024) throw new ReadAgentException("DraftLimit");
            }
        }
    }
    private static long SizeDraft(AgentReportDraft? draft) => draft is null ? 0 : 1024L + Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(draft)) * 4L;
    private long DraftBytes() => sessions.Values.Sum(s => SizeDraft(s.Draft)) + running.Values.Sum(r => r.DraftBytes);
    private static long Size(AgentTurn turn) => 1024L + Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(turn)) * 4L;
    private static long Size(Session session) => session.Bytes;
    private long TotalBytes() => sessions.Values.Sum(Size) + running.Values.Sum(r => r.Bytes);
    private static void Trim(Session session, ReadAgentLimits limits)
    {
        session.Bytes = session.History.Sum(Size);
        while (session.History.Count > 0 && (session.History.Count > limits.HistoryTurns || Size(session) > limits.HistoryBytes))
        { session.Bytes -= Size(session.History[0]); session.History.RemoveAt(0); session.Trimmed = true; }
    }
    public void Verify(Guid id, ActorSnapshot owner) { lock (gate) Require(id, owner); }
    // Stop one message; preserve completed context. The cancelled lease cannot commit a late answer.
    public void Cancel(Guid id, ActorSnapshot owner) { lock (gate) Require(id, owner).Run?.Cancel(); }
    public void Delete(Guid id, ActorSnapshot owner)
    { lock (gate) { var session = Require(id, owner); session.Run?.Cancel(); session.History.Clear(); sessions.Remove(id); } }
    public void ClearUser(Guid user)
    {
        lock (gate) foreach (var pair in sessions.Where(p => p.Value.Owner.UserId == user).ToArray())
        { pair.Value.Run?.Cancel(); pair.Value.History.Clear(); sessions.Remove(pair.Key); }
    }
    private void Prune()
    {
        foreach (var pair in sessions.Where(p => p.Value.End <= clock.GetUtcNow()).ToArray())
        { pair.Value.Run?.Cancel(); pair.Value.History.Clear(); sessions.Remove(pair.Key); }
    }
    private Session Require(Guid id, ActorSnapshot owner)
    {
        Prune();
        if (!owner.IsAuthenticated || owner.AccountId is null || !sessions.TryGetValue(id, out var value) || value.Owner != owner)
            throw new UnauthorizedAccessException();
        return value;
    }
    public sealed class Lease(CancellationTokenSource source, IReadOnlyList<AgentTurn> history, bool trimmed, bool invalidated,
        AgentReportDraft? draft, Func<AgentTurn, AgentReportDraft?, (bool Trimmed, bool Retained, int Count)> commit, Action invalidate, Action clearDraft, Action<AgentReportDraft> completeDraft, Action release) : IDisposable
    {
        public CancellationToken Token => source.Token;
        public IReadOnlyList<AgentTurn> History { get; } = history;
        public AgentReportDraft? Draft { get; } = draft;
        public bool Trimmed { get; } = trimmed;
        public bool Invalidated { get; } = invalidated;
        public (bool Trimmed, bool Retained, int Count) Commit(AgentTurn turn, AgentReportDraft? draft = null) => commit(turn, draft);
        public void CompleteDraft(AgentReportDraft value) => completeDraft(value);
        public void Invalidate() => invalidate();
        public void ClearDraft() => clearDraft();
        public void Dispose() { release(); source.Dispose(); }
    }
}
