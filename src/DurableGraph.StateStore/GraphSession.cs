using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One editable World with a publication-bound comparison baseline and stable live CLR identities.</summary>
/// <remarks>
/// Keep the graph stable throughout synchronous Commit. Later mutations are detected by the next Commit.
/// Dispose releases the editing session without saving. Domain mutations are never automatically rolled back.
/// </remarks>
public sealed class GraphSession<TWorld> : IDisposable where TWorld : DurableBase {
    private readonly GraphRepository _repository;
    private readonly WorldWorkspace<TWorld> _workspace;
    private bool _disposed;

    internal GraphSession(GraphRepository repository, WorldWorkspace<TWorld> workspace) {
        _repository = repository;
        _workspace = workspace;
    }

    public TWorld World => _workspace.World;
    /// <summary>The last installed World ID; null before the first successful Commit.</summary>
    public ObjectId? WorldId => _workspace.WorldId.IsNull ? null : _workspace.WorldId;
    public FrameAddress? ParentRevisionAddress => _workspace.ParentRevisionAddress;
    public bool IsFaulted => _repository.IsFaulted;

    public FrameAddress Commit(ReadAmplificationBaseBudgetParameters parameters) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _repository.Commit(this, _workspace, parameters);
    }

    public void Dispose() {
        if (_disposed) { return; }
        _repository.Release(this);
        _disposed = true;
    }
}
