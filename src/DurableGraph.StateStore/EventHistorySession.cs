using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One branch's editable State and optional recorded Event awaiting its next State.</summary>
/// <remarks>
/// Single-threaded. Keep graphs stable during Commit. Dispose does not save or roll back domain changes.
/// An Event is a snapshot: application code must not mutate its reachable content while processing it.
/// Cold Resume restores Event and State independently; hot caller-created aliases remain caller-owned.
/// </remarks>
public sealed class EventHistorySession<TState> : IDisposable where TState : DurableBase {
    private readonly EventHistoryRepository _repository;
    internal WorldWorkspace<TState> Workspace { get; }
    private bool _disposed;

    internal EventHistorySession(EventHistoryRepository repository, string branchName,
        WorldWorkspace<TState> workspace, GraphFrame? head, DurableBase? pendingEvent) {
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
    public DurableBase? PendingEvent { get; internal set; }
    public bool IsFaulted => _repository.IsFaulted;

    public TEvent GetPendingEvent<TEvent>() where TEvent : DurableBase {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PendingEvent as TEvent ?? throw new InvalidOperationException("No pending Event of the requested type.");
    }

    public GraphFrame CommitDomainEvent(DurableBase domainEvent, ReadAmplificationBaseBudgetParameters? parameters = null) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(domainEvent);
        return _repository.Commit(this, domainEvent, null, parameters);
    }

    public GraphFrame CommitDomainState(ReadAmplificationBaseBudgetParameters? parameters = null) => CommitDomainState(State, parameters);

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
