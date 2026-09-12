using Atelia.DurableGraph;
using Atelia.DurableGraph.Persistence;

namespace EventHistoryRecovery;

// Application example, shared with the fault tests. Call after a fresh Resume when recovering.
// Exceptions end this attempt: the caller disposes the session/repository and decides when to reopen.
internal static class PendingRecovery {
    public static bool Complete<TState>(EventHistorySession<TState> session,
        Action<TState> rebuildTransient, Action<TState, IDurableObject> apply) where TState : class, IDurableObject {
        rebuildTransient(session.State);
        if (session.PendingEvent is not { } pending) { return false; }
        apply(session.State, pending);
        session.CommitDomainState();
        return true;
    }
}
