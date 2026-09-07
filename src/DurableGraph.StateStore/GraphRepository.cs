using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using Atelia.DurableGraph.StateStore.Storage;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Owns a single persistent World head and at most one active editing session.</summary>
/// <remarks>
/// Single-threaded and exclusive: do not open or modify its files through other APIs while alive.
/// Commits flush Schema and State before publishing, then retain the existing domain instances.
/// Reopen strictly rejects malformed files, including incomplete uncommitted tails; no automatic
/// repair, branching or OS/power-loss guarantee is provided. Schema registrations are monotonic.
/// </remarks>
public sealed class GraphRepository : IDisposable {
    private readonly IRbfFile _publicationFile;
    private readonly IRbfFile _schemaFile;
    private readonly SegmentStore _segments;
    private readonly SchemaStore _schemas;
    private readonly StateRevisionStore _states;
    private readonly PublicationLog _publication;
    private object? _activeSession;
    private bool _busy;
    private bool _disposed;

    private GraphRepository(IRbfFile publicationFile, IRbfFile schemaFile, SegmentStore segments, SchemaStore schemas) {
        _publicationFile = publicationFile;
        _schemaFile = schemaFile;
        _segments = segments;
        _schemas = schemas;
        _states = new(segments);
        _publication = new(publicationFile, ValidatePublishedRevision);
        _publication.AfterAppend = () => Checkpoint?.Invoke(CommitCheckpoint.AfterPublicationAppend);
        // Schema and then State files were confirmed before this constructor. Confirm the
        // publication only after strict replay has also validated its entire State closure.
        publicationFile.DurableFlush();
    }

    public bool IsFaulted { get; private set; }
    public FrameAddress? HeadRevisionAddress { get { RequireAvailable(); return _publication.Head?.RevisionAddress; } }
    public uint? WorldId { get { RequireAvailable(); return _publication.Head?.WorldId; } }
    internal Action<CommitCheckpoint>? Checkpoint { get; set; }

    /// <summary>Creates a new repository directory. Existing paths are never overwritten or adopted.</summary>
    public static GraphRepository CreateNew(string path, RbfSegmentStoreOptions? options = null) => Open(path, options, create: true);

    /// <summary>Replays the publication log and validates its State references without recovering bad tails.</summary>
    public static GraphRepository OpenExisting(string path, RbfSegmentStoreOptions? options = null) => Open(path, options, create: false);

    private static GraphRepository Open(string path, RbfSegmentStoreOptions? options, bool create) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string root = Path.GetFullPath(path);
        if (create) {
            if (Directory.Exists(root) || File.Exists(root)) { throw new IOException("Repository path already exists."); }
            Directory.CreateDirectory(root);
        } else if (!Directory.Exists(root)) {
            throw new DirectoryNotFoundException(root);
        }
        IRbfFile? publication = null;
        IRbfFile? schemas = null;
        SegmentStore? segments = null;
        try {
            // This exclusive file handle also serializes repository opens, before other resources.
            string publicationPath = Path.Combine(root, "publication.rbf");
            publication = create ? RbfFile.CreateNew(publicationPath) : RbfFile.OpenExisting(publicationPath);
            string schemaPath = Path.Combine(root, "schemas.rbf");
            schemas = create ? RbfFile.CreateNew(schemaPath) : RbfFile.OpenExisting(schemaPath);
            SchemaStore schemaStore = new(schemas);
            // SchemaStore already flushes nonempty recovery; include a new empty header too.
            if (schemaStore.Count == 0) { schemas.DurableFlush(); }
            string statePath = Path.Combine(root, "state");
            RbfSegmentStoreOptions strict = StrictOptions(options);
            if (create) {
                segments = SegmentStore.CreateNew(statePath, strict);
                using RbfSegmentWriterLease writer = segments.OpenActiveWriter();
                writer.File.DurableFlush();
            } else {
                // Do this before SegmentStore owns/caches historical read handles. Confirm complete
                // files after uncertain prior outcomes; never select a recoverable prefix or truncate.
                if (!Directory.Exists(statePath)) { throw new DirectoryNotFoundException(statePath); }
                foreach (string filePath in Directory.EnumerateFiles(statePath, "*.rbf", SearchOption.AllDirectories)) {
                    using IRbfFile file = RbfFile.OpenExisting(filePath);
                    ValidatePhysicalFile(file);
                    file.DurableFlush();
                }
                segments = SegmentStore.OpenExisting(statePath, strict);
            }
            return new(publication, schemas, segments, schemaStore);
        } catch {
            segments?.Dispose();
            schemas?.Dispose();
            publication?.Dispose();
            // A failed first creation leaves its partial directory for inspection, not automatic deletion.
            throw;
        }
    }

    private static RbfSegmentStoreOptions StrictOptions(RbfSegmentStoreOptions? supplied) {
        supplied ??= new();
        return new() {
            NewStoreLayout = supplied.NewStoreLayout,
            SegmentSizeThresholdBytes = supplied.SegmentSizeThresholdBytes,
            HistoricalReaderPoolCapacity = supplied.HistoricalReaderPoolCapacity,
            CacheMode = supplied.CacheMode,
            RecoverActiveTailOnOpen = false,
        };
    }

    private static void ValidatePhysicalFile(IRbfFile file) {
        var frames = file.ScanForward(showTombstone: true).GetEnumerator();
        while (frames.MoveNext()) {
            using RbfPooledFrame frame = file.ReadPooledFrame(frames.Current.Ticket).Unwrap();
            if (frame.IsTombstone) { throw new InvalidDataException("Repository State files cannot contain tombstones."); }
        }
        if (frames.TerminationError is { } error) { throw new InvalidDataException($"Invalid State tail: {error.Message}"); }
    }

    private void ValidatePublishedRevision(FrameAddress? parent, PublicationHead head) {
        StateRevision revision = _states.Read(head.RevisionAddress);
        if (revision.ParentRevisionAddress != parent) { throw new InvalidDataException("Publication disagrees with the State Revision Parent."); }
        var objects = _states.ReadLiveObjectHeadMap(head.RevisionAddress);
        if (!objects.ContainsKey(head.WorldId)) { throw new InvalidDataException("Published World is absent from the selected Revision."); }
        // Check the complete reconstruction closure and exact Schema references without model callbacks.
        // TODO: Measure startup cost before caching repeated historical map/chain validation.
        foreach (uint id in objects.Keys) {
            ObjectVersionChain chain = _states.ReadObjectVersionChain(head.RevisionAddress, id);
            DecodedBaseObjectBody body = BaseObjectBodyCodec.Decode(chain.Records[0].Record.Body);
            if (body.Kind == ObjectStateKind.Durable) {
                if (_schemas.GetRequired(body.SchemaKey!.Value).Kind != SchemaKind.ReferenceObject) {
                    throw new InvalidDataException("An object Base cannot refer to an inline Schema.");
                }
            }
            else if (chain.Records.Count != 1) { throw new InvalidDataException("String cannot have a Delta chain."); }
            if (id == head.WorldId && body.Kind != ObjectStateKind.Durable) { throw new InvalidDataException("World must be a durable object."); }
        }
    }

    /// <summary>Starts editing a new non-null World; requires no previously published head.</summary>
    public GraphSession<TWorld> Create<TWorld>(TWorld world, StateModelRegistry models) where TWorld : DurableBase {
        RequireFreeSession();
        if (_publication.Head is not null) { throw new InvalidOperationException("An existing repository head must be loaded, not replaced by Create."); }
        _busy = true;
        try {
            var session = new GraphSession<TWorld>(this, WorldWorkspace<TWorld>.Create(_states, _schemas, world, models));
            _activeSession = session;
            return session;
        } finally { _busy = false; }
    }

    /// <summary>Loads the published World, using exact readers and single-object upgrades from the model directory.</summary>
    public GraphSession<TWorld> Load<TWorld>(StateModelRegistry models) where TWorld : DurableBase {
        RequireFreeSession();
        PublicationHead head = _publication.Head ?? throw new InvalidOperationException("There is no published World to load.");
        _busy = true;
        try {
            var session = new GraphSession<TWorld>(this,
                WorldWorkspace<TWorld>.Load(_states, _schemas, head.RevisionAddress, head.WorldId, models));
            _activeSession = session;
            return session;
        } finally { _busy = false; }
    }

    internal FrameAddress Commit<TWorld>(GraphSession<TWorld> session, WorldWorkspace<TWorld> workspace,
        ReadAmplificationBaseBudgetParameters parameters) where TWorld : DurableBase {
        RequireAvailable();
        if (!ReferenceEquals(_activeSession, session) || workspace.ParentRevisionAddress != _publication.Head?.RevisionAddress) {
            throw new InvalidOperationException("The session does not own the expected published Parent.");
        }
        _busy = true;
        FrameAddress? address = null;
        bool stateWriting = false;
        bool publicationStarted = false;
        bool published = false;
        try {
            using PreparedWorldSave<TWorld> pending = workspace.Stage(parameters);
            Checkpoint?.Invoke(CommitCheckpoint.AfterPrepare);
            stateWriting = true;
            Checkpoint?.Invoke(CommitCheckpoint.BeforeStateAppend);
            address = _states.AppendDurably(pending.Revision);
            stateWriting = false;
            Checkpoint?.Invoke(CommitCheckpoint.AfterStateDurable);
            pending.PrepareInstall(address.Value);
            PublicationHead next = new(address.Value, pending.WorldId);
            Checkpoint?.Invoke(CommitCheckpoint.BeforePublication);
            publicationStarted = true;
            _publication.Publish(workspace.ParentRevisionAddress, next);
            published = true;
            Checkpoint?.Invoke(CommitCheckpoint.AfterPublication);
            pending.Install();
            return address.Value;
        } catch (Exception error) {
            if (stateWriting || publicationStarted || _schemas.IsFaulted) { IsFaulted = true; }
            if (!stateWriting && !publicationStarted && address is null) { throw; }
            throw new GraphCommitException(published ? GraphCommitOutcome.Published :
                publicationStarted ? GraphCommitOutcome.Unknown : GraphCommitOutcome.NotPublished, address, error);
        } finally { _busy = false; }
    }

    internal void Release(object session) {
        if (_busy) { throw new InvalidOperationException("Cannot dispose a session during a repository operation."); }
        if (ReferenceEquals(_activeSession, session)) { _activeSession = null; }
    }

    private void RequireFreeSession() {
        RequireAvailable();
        if (_activeSession is not null) { throw new InvalidOperationException("Only one editing session may be active."); }
    }

    private void RequireAvailable() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsFaulted) { throw new InvalidOperationException("The repository is faulted; dispose and reopen to determine its published state."); }
        if (_busy) { throw new InvalidOperationException("Repository operations cannot be reentered."); }
    }

    public void Dispose() {
        if (_disposed) { return; }
        if (_busy) { throw new InvalidOperationException("Cannot dispose a busy repository."); }
        _disposed = true;
        _activeSession = null;
        try { _segments.Dispose(); }
        finally { try { _schemaFile.Dispose(); } finally { _publicationFile.Dispose(); } }
    }
}
