using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.RbfSegmentStore;

namespace Atelia.DurableGraph.Tests;

// Test-only adapter for existing generated model/history witnesses. Each old Save operation
// maps to initial S0 or explicit marker E + S. It preserves the graph assertions without
// retaining a product compatibility facade. Public API acceptance uses EventHistory directly.
public sealed class FixtureGraphRepository : IDisposable {
    private readonly EventHistoryRepository _repository;
    private FrameAddress? _stateAddress;
    private FixtureGraphRepository(EventHistoryRepository repository, bool existing) {
        _repository = repository;
        if (existing) { _stateAddress = repository.GetHead("main").RevisionAddress; }
    }
    public static FixtureGraphRepository CreateNew(string path, RbfSegmentStoreOptions? options = null) =>
        new(EventHistoryRepository.CreateNew(path, options), false);
    public static FixtureGraphRepository OpenExisting(string path, RbfSegmentStoreOptions? options = null) =>
        new(EventHistoryRepository.OpenExisting(path, options), true);
    public FrameAddress? HeadRevisionAddress => _stateAddress;
    public bool IsFaulted => _repository.IsFaulted;
    public FixtureGraphSession<T> Create<T>(T state, StateModelRegistry models) where T : DurableBase {
        FixtureMarker.Register(models);
        return new(this, state, models);
    }
    public FixtureGraphSession<T> Load<T>(StateModelRegistry models) where T : DurableBase {
        FixtureMarker.Register(models);
        return new(this, _repository.Resume<T>("main", models));
    }
    internal EventHistorySession<T> Initialize<T>(T state, StateModelRegistry models,
        ReadAmplificationBaseBudgetParameters parameters) where T : DurableBase =>
        _repository.CreateBranch("main", state, models, parameters);
    internal void Accept(FrameAddress address) => _stateAddress = address;
    public void Dispose() => _repository.Dispose();
}

public sealed class FixtureGraphSession<T> : IDisposable where T : DurableBase {
    private readonly FixtureGraphRepository _repository;
    private readonly T _initial;
    private readonly StateModelRegistry? _models;
    private EventHistorySession<T>? _session;
    internal FixtureGraphSession(FixtureGraphRepository repository, T initial, StateModelRegistry models) {
        _repository = repository; _initial = initial; _models = models;
    }
    internal FixtureGraphSession(FixtureGraphRepository repository, EventHistorySession<T> session) {
        _repository = repository; _initial = session.State; _session = session;
    }
    public T World => _session is null ? _initial : _session.State;
    public ObjectId? WorldId => _session?.StateId;
    public FrameAddress? ParentRevisionAddress => _session?.StateRevisionAddress;
    public FrameAddress Commit(ReadAmplificationBaseBudgetParameters parameters) {
        if (_session is null) {
            _session = _repository.Initialize(_initial, _models!, parameters);
        } else {
            if (_session.PendingEvent is null) { _session.CommitDomainEvent(new FixtureMarker(), parameters); }
            _session.CommitDomainState(parameters);
        }
        FrameAddress address = _session.StateRevisionAddress;
        _repository.Accept(address);
        return address;
    }
    public void Dispose() => _session?.Dispose();
}

internal sealed class FixtureMarker : DurableBase {
    private static readonly DurableSchema Schema = new("DurableGraph.Tests.FixtureMarker", 1,
        new DurableFieldInfo(1, TypeTag.Byte));
    private static readonly StateModelBinding Model = new StateModelBinding<FixtureMarker, byte>(
        new CapturedStatePreparation<byte>(Schema,
            static (in byte value) => new PreparedBaseBody(new byte[] { value }),
            static (in byte prior, in byte next) => new PreparedDeltaBody(prior != next, new byte[] { next })),
        [new StateReaderBinding<byte>(Schema,
            static (ref BinaryPayloadReader reader) => reader.ReadByte(),
            static (ref BinaryPayloadReader reader, in byte prior) => reader.ReadByte(), Visit)],
        static row => row.GetState<byte>(),
        static () => new FixtureMarker(),
        static (FixtureMarker domain, in byte value, ObjectReadTable objects) => { },
        static (domain, context) => 0,
        Visit);
    private static void Visit(in byte state, IStateReferenceVisitor visitor) { }
    internal static void Register(StateModelRegistry models) => models.Register(Model);
}
