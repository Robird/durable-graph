using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One branch's editable State and optional recorded Event awaiting its next State.</summary>
/// <remarks>
/// Single-threaded. Keep graphs stable during Commit. Dispose does not save or roll back domain changes.
/// An Event is a snapshot: application code must not mutate its reachable content while processing it.
/// Cold Resume restores Event and State independently; hot caller-created aliases remain caller-owned.
/// </remarks>
public sealed class EventHistorySession<TState> : IDisposable where TState : class, IDurableObject {
    private readonly EventHistoryRepository _repository;
    internal WorldWorkspace<TState> Workspace { get; }
    private bool _disposed;

    internal EventHistorySession(EventHistoryRepository repository, string branchName,
        WorldWorkspace<TState> workspace, GraphFrame? head, IDurableObject? pendingEvent) {
        _repository = repository;
        BranchName = branchName;
        Workspace = workspace;
        Head = head!; // Initial creation publishes before this session is delivered.
        PendingEvent = pendingEvent;
    }

    public string BranchName { get; }
    public TState State => Workspace.World;
    public ObjectId StateId => Workspace.WorldId;
    public FrameAddress StateRevisionAddress => Workspace.ParentRevisionAddress!.Value;
    public GraphFrame Head { get; internal set; }
    /// <summary>The recorded Event awaiting its next State, or null when the head is a State.</summary>
    /// <remarks>
    /// After a successful Event commit this is the original supplied instance, not a cloned graph.
    /// Keep its reachable content read-only, including any aliases shared with State. Resume restores
    /// mutable State and Event graphs independently. After a failed commit, use a reopened session
    /// to determine the published head; this property's old value is not recovery evidence.
    /// </remarks>
    public IDurableObject? PendingEvent { get; internal set; }
    /// <summary>Whether the owning repository has faulted and must be disposed and reopened.</summary>
    /// <remarks>
    /// This is independent of <see cref="GraphCommitException.Outcome"/>: NotPublished can still be faulted.
    /// A healthy repository does not imply that application mutations were rolled back.
    /// </remarks>
    public bool IsFaulted => _repository.IsFaulted;

    public TEvent GetPendingEvent<TEvent>() where TEvent : class, IDurableObject {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PendingEvent as TEvent ?? throw new InvalidOperationException("No pending Event of the requested type.");
    }

    /// <summary>Publishes an Event after the current State and retains it as PendingEvent.</summary>
    /// <param name="domainEvent">The Event root to capture; keep its reachable content stable during capture and read-only afterward.</param>
    /// <param name="parameters">Policy for this call only; null uses the library default, not a previous call's override.</param>
    /// <returns>The published Event handle owned by this open repository.</returns>
    /// <remarks>
    /// Commits strictly alternate Event and State. Normal return advances Head and sets PendingEvent
    /// to the supplied instance; it does not advance the committed State baseline or replace State's
    /// domain instances. Persisted capture does not freeze caller-owned CLR objects or their aliases.
    /// A failure can occur after publication but before delivery. Inspect IsFaulted independently of
    /// any GraphCommitException.Outcome. Pre-append failures may propagate their original exception;
    /// no failure automatically rolls back domain mutations. Recover using newly restored graphs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">domainEvent is null.</exception>
    /// <exception cref="InvalidOperationException">The session cannot commit, including when its head is already an Event.</exception>
    /// <exception cref="GraphCommitException">An append/publication attempt failed; its Outcome describes publication, not session health.</exception>
    public GraphFrame CommitDomainEvent(IDurableObject domainEvent, ReadAmplificationBaseBudgetParameters? parameters = null) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(domainEvent);
        return _repository.Commit(this, domainEvent, null, parameters);
    }

    /// <summary>Publishes the current State after the pending Event, installs its baseline and clears PendingEvent.</summary>
    /// <param name="parameters">Policy for this call only; null uses the library default, not a previous call's override.</param>
    /// <returns>The published State handle owned by this open repository.</returns>
    /// <remarks>
    /// Requires an Event head. Normal return retains the current domain instances, advances the State
    /// baseline and Head, and clears PendingEvent. Publication can succeed before installation or
    /// delivery fails. Do not infer rollback from an exception or retry against the old domain graph.
    /// Inspect IsFaulted separately from GraphCommitException.Outcome; even NotPublished may be faulted.
    /// Pre-append failures can propagate their original exception. Dispose and reopen a faulted repository,
    /// Resume, then process only the newly restored PendingEvent, if any; a State head requires no replay.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The session cannot commit, including when there is no pending Event.</exception>
    /// <exception cref="GraphCommitException">An append/publication attempt failed; a Published outcome requires recovery without blindly replaying the Event.</exception>
    public GraphFrame CommitDomainState(ReadAmplificationBaseBudgetParameters? parameters = null) => CommitDomainState(State, parameters);

    /// <summary>Publishes a replacement State after the pending Event, installs it and clears PendingEvent.</summary>
    /// <param name="nextState">The replacement root, whose exact runtime type must equal TState; its supplied instances are retained on success.</param>
    /// <param name="parameters">Policy for this call only; null uses the library default, not a previous call's override.</param>
    /// <returns>The published State handle owned by this open repository.</returns>
    /// <remarks>
    /// Requires an Event head. Normal return installs the captured replacement and its baseline,
    /// advances Head and clears PendingEvent. Publication can succeed before installation or delivery
    /// fails. Do not infer rollback from an exception or retry against old State/Event references.
    /// Inspect IsFaulted separately from GraphCommitException.Outcome; even NotPublished may be faulted.
    /// Pre-append failures can propagate their original exception. Dispose and reopen a faulted repository,
    /// Resume, then process only the newly restored PendingEvent, if any; a State head requires no replay.
    /// </remarks>
    /// <exception cref="ArgumentNullException">nextState is null.</exception>
    /// <exception cref="ArgumentException">nextState does not have the session's exact State type.</exception>
    /// <exception cref="InvalidOperationException">The session cannot commit, including when there is no pending Event.</exception>
    /// <exception cref="GraphCommitException">An append/publication attempt failed; a Published outcome requires recovery without blindly replaying the Event.</exception>
    public GraphFrame CommitDomainState(TState nextState, ReadAmplificationBaseBudgetParameters? parameters = null) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nextState);
        return _repository.Commit(this, null, nextState, parameters);
    }

    public void Dispose() {
        if (_disposed) { return; }
        _repository.Release(this);
        _disposed = true;
    }
}
