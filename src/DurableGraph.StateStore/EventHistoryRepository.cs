using Atelia.DurableGraph.StateStore.Storage;
using Atelia.EventJournal;
using Atelia.RbfSegmentStore;
using FrameAddress = Atelia.DurableGraph.StateStore.Storage.FrameAddress;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Owns Schema/State resources and a Journal whose named refs are the sole publication authority.</summary>
/// <remarks>
/// Single-threaded, one writer and one active editing session. Close a session before moving or forking refs.
/// Strict reopening rejects damaged tails; no automatic repair, transparent retry or power-loss guarantee.
/// Handles belong to one open repository instance. Read results are caller-enforced read-only snapshots.
/// </remarks>
public sealed class EventHistoryRepository : IDisposable {
    private readonly HistoryJournal _history;
    private readonly GraphResources _resources;
    private readonly object _identity = new();
    private object? _activeSession;
    private bool _busy;
    private bool _disposed;
    private static readonly ReadAmplificationBaseBudgetParameters DefaultPolicy = new(3, 5);
    internal Action<CommitCheckpoint>? Checkpoint { get; set; }

    private EventHistoryRepository(HistoryJournal history, GraphResources resources) {
        _history = history;
        _resources = resources;
        ValidateHistory();
        if (!resources.IsReadOnly) { history.ConfirmDurable(); }
    }

    public bool IsReadOnly => _resources.IsReadOnly;
    /// <summary>Whether this repository has faulted and must be disposed and reopened before further use.</summary>
    /// <remarks>
    /// Independent of GraphCommitException.Outcome: a NotPublished attempt can still fault the writer.
    /// False does not mean application mutations were rolled back. Recovery must inspect persisted
    /// history through a newly opened repository rather than reuse old domain objects or frame handles.
    /// </remarks>
    public bool IsFaulted => _resources.IsFaulted;
    public static EventHistoryRepository CreateNew(string path, RbfSegmentStoreOptions? options = null) => Open(path, options, true, false);
    public static EventHistoryRepository OpenExisting(string path, RbfSegmentStoreOptions? options = null) => Open(path, options, false, false);
    public static EventHistoryRepository OpenReadOnlyExisting(string path, RbfSegmentStoreOptions? options = null) => Open(path, options, false, true);

    private static EventHistoryRepository Open(string path, RbfSegmentStoreOptions? options, bool create, bool readOnly) {
        HistoryJournal? history = null;
        GraphResources? resources = null;
        try {
            history = create ? HistoryJournal.Create(path) : HistoryJournal.Open(path, readOnly);
            resources = create ? GraphResources.CreateInExistingDirectory(path, options) : readOnly
                ? GraphResources.OpenReadOnlyExisting(path, options) : GraphResources.OpenExisting(path, options);
            return new(history, resources);
        } catch {
            try { resources?.Dispose(); } finally { history?.Dispose(); }
            throw;
        }
    }

    /// <summary>Publishes S0 and returns a session retaining the supplied domain instances.</summary>
    /// <typeparam name="TState">The exact domain type of the initial State root.</typeparam>
    /// <param name="branchName">A new, nonempty branch name.</param>
    /// <param name="initialState">The nonnull initial State root; its instances are retained rather than cloned.</param>
    /// <param name="models">The model and history capabilities to freeze for the session.</param>
    /// <param name="parameters">Policy for this initial save only; null uses the library default. It does not set later Commit defaults.</param>
    /// <returns>An active session whose S0 is already published, with no PendingEvent.</returns>
    /// <remarks>
    /// Requires a writable repository with no active session. This creates a saved initial State, not
    /// an empty branch. Keep the graph stable during capture. Failure can occur after publication
    /// but before a session is delivered; do not blindly retry branch creation. Check IsFaulted
    /// independently of GraphCommitException.Outcome and reopen a faulted repository to inspect
    /// persisted history. Pre-append failures can propagate their original exception, and domain
    /// mutations are never automatically rolled back.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A session is already active, the branch already exists, or the repository cannot perform the operation.</exception>
    /// <exception cref="GraphCommitException">An append/publication attempt failed; the branch may already have been published.</exception>
    public EventHistorySession<TState> CreateBranch<TState>(string branchName, TState initialState,
        StateModelRegistry models, ReadAmplificationBaseBudgetParameters? parameters = null) where TState : DurableBase {
        RequireFreeWriter();
        ValidateNewName(branchName);
        _busy = true;
        try {
            var workspace = WorldWorkspace<TState>.Create(_resources.States, _resources.Schemas, initialState, models);
            var session = new EventHistorySession<TState>(this, branchName, workspace, null, null);
            Publish(session, null, initialState, parameters ?? DefaultPolicy, initial: true);
            _activeSession = session;
            return session;
        } finally { _busy = false; }
    }

    /// <summary>Restores the chosen branch without replaying business handlers. An E head restores its preceding S too.</summary>
    /// <typeparam name="TState">The exact current domain type of the State root.</typeparam>
    /// <param name="branchName">The existing branch to resume at its persisted head.</param>
    /// <param name="models">The model, historical reader and Upgrade capabilities to freeze for this session.</param>
    /// <returns>An editable State session with PendingEvent populated only when the persisted head is an Event.</returns>
    /// <remarks>
    /// Requires a writable repository with no active session. At an Event head, State is restored from
    /// the preceding State revision and PendingEvent from the Event revision, with independently
    /// allocated mutable graphs. Resume runs decoding/Upgrade/materialization but no business handler;
    /// application code must rebuild Transient state (constructors and field initializers are not run
    /// during domain restoration). Process only the newly restored PendingEvent, if present. A State
    /// head has no pending work to replay, but does not by itself identify a completed external request.
    /// If Open or Resume fails, stop and report the failure; this API does not fall back to an older head.
    /// </remarks>
    /// <exception cref="ArgumentNullException">models is null.</exception>
    /// <exception cref="InvalidOperationException">A session is already active or the repository cannot perform the operation.</exception>
    public EventHistorySession<TState> Resume<TState>(string branchName, StateModelRegistry models) where TState : DurableBase {
        RequireFreeWriter();
        ArgumentNullException.ThrowIfNull(models);
        _busy = true;
        try {
            GraphFrame head = HeadCore(branchName);
            GraphFrame state = head.Kind == GraphFrameKind.State ? head : PreviousStateCore(head);
            StateModelSnapshot snapshot = models.Snapshot(_resources.Schemas);
            RevisionReadSession reads = new(_resources.States, _resources.Schemas, snapshot);
            var workspace = WorldWorkspace<TState>.LoadSnapshot(reads, state.RevisionAddress, state.RootId);
            DurableBase? pending = head.Kind == GraphFrameKind.Event
                ? GraphReader.Read<DurableBase>(reads, head.RevisionAddress, head.RootId).Root : null;
            var session = new EventHistorySession<TState>(this, branchName, workspace, head, pending);
            _activeSession = session;
            return session;
        } finally { _busy = false; }
    }

    public IReadOnlyList<string> ListBranches() { RequireAvailable(); return _history.Journal.ListBranches(); }
    public GraphFrame GetHead(string branchName) { RequireAvailable(); return HeadCore(branchName); }

    /// <summary>Fully materializes the selected logical chain from oldest to newest, including State and Event handles.</summary>
    /// <param name="branchName">The branch whose head is obtained once for this operation.</param>
    /// <returns>A fully materialized, chronological list of handles owned by this open repository.</returns>
    /// <remarks>
    /// Reads the complete ancestor chain of the selected head; excludes physical orphan appends and
    /// records unique to other branches. No domain objects are restored. Taking the last few results
    /// does not avoid full-chain reading or allocation: this is not a paginated or lazy API.
    /// Opening the repository separately validates physical Journal records and referenced revisions,
    /// including orphans; that validation cost is distinct from this logical-chain enumeration.
    /// </remarks>
    public IReadOnlyList<GraphFrame> ReadFrames(string branchName) {
        RequireAvailable();
        GraphFrame head = HeadCore(branchName);
        return _history.Journal.ReadChronologicalChain(head.Address, checkedRead: true).Unwrap()
            .Select(address => Issue(_history.Read(address))).ToArray();
    }

    /// <summary>Fully materializes the branch's logical chain, then returns its Events from oldest to newest.</summary>
    /// <param name="branchName">The branch whose head is obtained once for this operation.</param>
    /// <returns>A fully materialized, chronological Event list owned by this open repository.</returns>
    /// <remarks>
    /// Uses ReadFrames and filters by Event role, excluding orphan appends and records unique to other
    /// branches. No domain objects are restored. Taking only the latest N results still incurs the
    /// complete chain read and materialization; this API supplies no pagination benefit. Repository
    /// opening has separate full physical-history validation, including orphan records.
    /// </remarks>
    public IReadOnlyList<GraphFrame> ReadEvents(string branchName) => ReadFrames(branchName).Where(frame => frame.Kind == GraphFrameKind.Event).ToArray();
    public GraphFrame GetPreviousState(GraphFrame eventFrame) {
        RequireAvailable();
        CheckFrame(eventFrame, GraphFrameKind.Event);
        return PreviousStateCore(eventFrame);
    }

    public TEvent ReadEvent<TEvent>(GraphFrame frame, StateModelRegistry models) where TEvent : DurableBase => Read<TEvent>(frame, models, GraphFrameKind.Event);
    public TState ReadState<TState>(GraphFrame frame, StateModelRegistry models) where TState : DurableBase => Read<TState>(frame, models, GraphFrameKind.State);

    private T Read<T>(GraphFrame frame, StateModelRegistry models, GraphFrameKind kind) where T : DurableBase {
        RequireAvailable();
        CheckFrame(frame, kind);
        ArgumentNullException.ThrowIfNull(models);
        _busy = true;
        try {
            return GraphReader.Read<T>(_resources.States, _resources.Schemas, frame.RevisionAddress,
                frame.RootId, models.Snapshot(_resources.Schemas)).Root;
        } finally { _busy = false; }
    }

    /// <summary>Experimental read-only pair without requiring the current root types in advance.</summary>
    /// <remarks>
    /// First and Second follow input order, independently of each frame's State/Event kind.
    /// Actual root types are preserved. Both graphs must be treated as read-only; cross-graph
    /// instance identity is not guaranteed, and both reads must succeed before delivery.
    /// Application callback side effects are not rolled back.
    /// </remarks>
    public (DurableBase First, DurableBase Second) ReadPair(GraphFrame first, GraphFrame second,
        StateModelRegistry models) => ReadPair<DurableBase, DurableBase>(first, second, models);

    /// <summary>Experimental pair of read-only snapshots with caller-specified root type checks.</summary>
    /// <remarks>
    /// Both reads must succeed before delivery. Shared instances are possible: callers must
    /// treat both graphs as read-only. Application callback side effects are not rolled back.
    /// </remarks>
    public (TFirst First, TSecond Second) ReadPair<TFirst, TSecond>(GraphFrame first, GraphFrame second,
        StateModelRegistry models) where TFirst : DurableBase where TSecond : DurableBase {
        RequireAvailable();
        CheckFrame(first);
        CheckFrame(second);
        ArgumentNullException.ThrowIfNull(models);
        _busy = true;
        try {
            return GraphReader.ReadPair<TFirst, TSecond>(_resources.States, _resources.Schemas,
                first.RevisionAddress, first.RootId, second.RevisionAddress, second.RootId, models.Snapshot(_resources.Schemas));
        } finally { _busy = false; }
    }

    /// <summary>Creates a named branch at any checked historical Event or State, without moving the source branch.</summary>
    public GraphFrame CreateBranch(string branchName, GraphFrame selectedFrame) {
        RequireFreeWriter();
        ValidateNewName(branchName);
        CheckFrame(selectedFrame);
        MutateRef(() => _history.Journal.CreateBranch(branchName, selectedFrame.Address));
        return selectedFrame;
    }

    /// <summary>Explicit compare-and-swap movement; requires closing the old editing session first.</summary>
    public void MoveBranch(string branchName, GraphFrame expectedHead, GraphFrame target) {
        RequireFreeWriter();
        CheckFrame(expectedHead);
        CheckFrame(target);
        RefId branch = _history.Journal.OpenBranch(branchName).Unwrap();
        if (_history.Journal.GetHead(branch) != expectedHead.Address) { throw new InvalidOperationException("Branch head no longer matches expectedHead."); }
        MutateRef(() => _history.Journal.MoveRef(branch, expectedHead.Address, target.Address));
    }

    private void MutateRef<T>(Func<AteliaResult<T>> mutation) where T : notnull {
        _busy = true;
        bool attempted = false;
        bool published = false;
        try {
            Checkpoint?.Invoke(CommitCheckpoint.BeforePublication);
            attempted = true;
            var result = mutation();
            if (result.IsFailure && result.Error is EventJournalError { ErrorName: "RefCasMismatch" }) { attempted = false; }
            result.Unwrap();
            published = true;
            Checkpoint?.Invoke(CommitCheckpoint.AfterPublication);
        } catch (Exception error) {
            if (attempted) { _resources.MarkFaulted(); }
            throw new GraphCommitException(published ? GraphCommitOutcome.Published : attempted ? GraphCommitOutcome.Unknown : GraphCommitOutcome.NotPublished, null, error);
        } finally { _busy = false; }
    }

    internal GraphFrame Commit<TState>(EventHistorySession<TState> session, DurableBase? domainEvent,
        TState? nextState, ReadAmplificationBaseBudgetParameters? parameters) where TState : DurableBase {
        RequireAvailable();
        _resources.RequireWritable();
        if (!ReferenceEquals(_activeSession, session) || HeadCore(session.BranchName).Address != session.Head.Address) {
            throw new InvalidOperationException("Session does not own the expected branch head.");
        }
        bool isEvent = domainEvent is not null;
        if (isEvent != (session.Head.Kind == GraphFrameKind.State)) { throw new InvalidOperationException("Commits must alternate Event and State."); }
        GraphFrame state = isEvent ? session.Head : PreviousStateCore(session.Head);
        if (session.Workspace.ParentRevisionAddress != state.RevisionAddress) { throw new InvalidOperationException("Session State baseline does not match the Journal chain."); }
        _busy = true;
        try { return Publish(session, domainEvent, nextState, parameters ?? DefaultPolicy, initial: false); }
        finally { _busy = false; }
    }

    private GraphFrame Publish<TState>(EventHistorySession<TState> session, DurableBase? domainEvent,
        TState? nextState, ReadAmplificationBaseBudgetParameters parameters, bool initial) where TState : DurableBase {
        FrameAddress? address = null;
        bool writeAttempted = false;
        bool refAttempted = false;
        bool published = false;
        try {
            using PreparedWorldSave<TState> pending = domainEvent is null
                ? session.Workspace.Stage(nextState!, parameters) : session.Workspace.StageSnapshot(domainEvent, parameters);
            Checkpoint?.Invoke(CommitCheckpoint.AfterPrepare);
            Checkpoint?.Invoke(CommitCheckpoint.BeforeStateAppend);
            writeAttempted = true;
            address = _resources.States.AppendDurably(pending.Revision);
            Checkpoint?.Invoke(CommitCheckpoint.AfterStateDurable);
            if (domainEvent is null) { pending.PrepareInstall(address.Value); }
            GraphFrameKind kind = domainEvent is null ? GraphFrameKind.State : GraphFrameKind.Event;
            Checkpoint?.Invoke(CommitCheckpoint.BeforeJournalAppend);
            EventAddress journalAddress = _history.Append(kind, address.Value, pending.RootId, initial ? null : session.Head.Address);
            Checkpoint?.Invoke(CommitCheckpoint.AfterJournalDurable);
            GraphFrame frame = Issue(_history.Read(journalAddress));
            RefId branch = initial ? default : _history.Journal.OpenBranch(session.BranchName).Unwrap();
            Checkpoint?.Invoke(CommitCheckpoint.BeforePublication);
            refAttempted = true;
            if (initial) { _history.Journal.CreateBranch(session.BranchName, journalAddress).Unwrap(); }
            else {
                var result = _history.Journal.AdvanceRef(branch, session.Head.Address, journalAddress);
                if (result.IsFailure && result.Error is EventJournalError { ErrorName: "RefCasMismatch" }) { refAttempted = false; }
                result.Unwrap();
            }
            published = true;
            Checkpoint?.Invoke(CommitCheckpoint.AfterPublication);
            Checkpoint?.Invoke(CommitCheckpoint.BeforeInstall);
            if (domainEvent is null) { pending.Install(); }
            session.Head = frame;
            session.PendingEvent = domainEvent;
            return frame;
        } catch (Exception error) {
            if (writeAttempted || refAttempted || _resources.Schemas.IsFaulted) { _resources.MarkFaulted(); }
            if (!writeAttempted && !refAttempted && address is null) { throw; }
            throw new GraphCommitException(published ? GraphCommitOutcome.Published : refAttempted ? GraphCommitOutcome.Unknown : GraphCommitOutcome.NotPublished, address, error);
        }
    }

    private GraphFrame HeadCore(string branchName) {
        RefId branch = _history.Journal.OpenBranch(branchName).Unwrap();
        EventAddress head = _history.Journal.GetHead(branch) ?? throw new InvalidDataException("EventHistory branches cannot have empty heads.");
        return Issue(_history.Read(head));
    }
    private GraphFrame PreviousStateCore(GraphFrame frame) {
        if (frame.Parent is not { } parent) { throw new InvalidDataException("Event must have a preceding State."); }
        GraphFrame state = Issue(_history.Read(parent));
        if (state.Kind != GraphFrameKind.State) { throw new InvalidDataException("Event Parent is not State."); }
        return state;
    }
    private GraphFrame Issue(HistoryGraphRecord record) => new(_identity, record);
    private void CheckFrame(GraphFrame frame, GraphFrameKind? kind = null) {
        ArgumentNullException.ThrowIfNull(frame);
        if (!ReferenceEquals(frame.Owner, _identity)) { throw new ArgumentException("Frame was issued by another repository instance.", nameof(frame)); }
        if (kind is not null && frame.Kind != kind) { throw new ArgumentException("Frame has the wrong Event/State role.", nameof(frame)); }
    }
    private void ValidateNewName(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var result = _history.Journal.OpenBranch(name);
        if (result.IsSuccess) { throw new InvalidOperationException("Branch name already exists."); }
        if (result.Error is not EventJournalError { ErrorName: "BranchNotFound" }) {
            throw new ArgumentException(result.Error!.Message, nameof(name));
        }
    }

    private void ValidateHistory() {
        var records = _history.ReadAllFrames().ToDictionary(record => record.Address);
        foreach (HistoryGraphRecord record in records.Values) {
            FrameAddress? expectedParent = null;
            if (record.Parent is { } parentAddress) {
                if (!records.TryGetValue(parentAddress, out HistoryGraphRecord? parent) || parent.Kind == record.Kind) {
                    throw new InvalidDataException("Journal must alternate State and Event along its exact Parent chain.");
                }
                if (record.Kind == GraphFrameKind.Event) { expectedParent = parent.RevisionAddress; }
                else {
                    if (parent.Parent is not { } previousAddress || !records.TryGetValue(previousAddress, out HistoryGraphRecord? previous) || previous.Kind != GraphFrameKind.State) {
                        throw new InvalidDataException("A succeeding State requires Event's preceding State.");
                    }
                    expectedParent = previous.RevisionAddress;
                }
            } else if (record.Kind != GraphFrameKind.State) { throw new InvalidDataException("A Journal chain must begin with State."); }
            ValidateGraph(record, expectedParent);
        }
        foreach (string branch in _history.Journal.ListBranches()) { _ = HeadCore(branch); }
    }

    private void ValidateGraph(HistoryGraphRecord record, FrameAddress? expectedParent) {
        StateRevision revision = _resources.States.Read(record.RevisionAddress);
        if (revision.ParentRevisionAddress != expectedParent) { throw new InvalidDataException("Journal and StateRevision Parent disagree."); }
        var objects = _resources.States.ReadLiveObjectHeadMap(record.RevisionAddress);
        if (!objects.ContainsKey(record.RootId.Value)) { throw new InvalidDataException("Graph root is absent from Revision membership."); }
        foreach (uint id in objects.Keys) {
            ObjectVersionChain chain = _resources.States.ReadObjectVersionChain(record.RevisionAddress, id);
            DecodedBaseObjectBody body = BaseObjectBodyCodec.Decode(chain.Records[0].Record.Body, _resources.Schemas);
            if (body.Kind == ObjectStateKind.Durable && body.Layout.Schema!.Kind != SchemaKind.ReferenceObject) { throw new InvalidDataException("Object Base cannot select an inline Schema."); }
            if (body.Kind == ObjectStateKind.String && chain.Records.Count != 1) { throw new InvalidDataException("String cannot have a Delta chain."); }
            if (id == record.RootId.Value && body.Kind != ObjectStateKind.Durable) { throw new InvalidDataException("Graph root must be durable."); }
        }
    }

    internal void Release(object session) {
        if (_busy) { throw new InvalidOperationException("Cannot dispose a session during a repository operation."); }
        if (ReferenceEquals(_activeSession, session)) { _activeSession = null; }
    }
    private void RequireFreeWriter() {
        RequireAvailable();
        _resources.RequireWritable();
        if (_activeSession is not null) { throw new InvalidOperationException("Close the active session before creating, moving or resuming a branch."); }
    }
    private void RequireAvailable() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.RequireAvailable();
        if (_busy) { throw new InvalidOperationException("Repository operations cannot be reentered."); }
    }
    public void Dispose() {
        if (_disposed) { return; }
        if (_busy) { throw new InvalidOperationException("Cannot dispose a busy repository."); }
        _disposed = true;
        _activeSession = null;
        try { _resources.Dispose(); } finally { _history.Dispose(); }
    }
}
