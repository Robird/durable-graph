using Atelia.DurableGraph;
using BaseStates = Atelia.DurableGraph.Generated.Family_4842617365;
using PairStates = Atelia.DurableGraph.Generated.Family_4850616972;
using PointStates = Atelia.DurableGraph.Generated.Family_48506F696E74;

namespace InheritanceLibrary.Base;

#if HISTORY_V1
[DurableType("HPair", 1)] internal partial struct InternalPair<T> {
    [DurableField(1)] public T Value;
    [DurableField(2)] public int Stamp;
}
[DurableType("HPoint", 1)] internal partial struct InternalPoint {
    [DurableField(1)] public int Value;
}
[DurableType("HBase", 1)] public abstract partial class LegacyBase<T> : IDurableObject where T : struct {
    [DurableField(1)] private readonly InternalPair<T> _pair;
    [DurableField(2)] private readonly InternalPoint? _optional;
    [DurableField(4)] public LegacyBase<T>? Link;
    protected LegacyBase(T value, bool hasOptional) {
        _pair = new() { Value = value, Stamp = 20 };
        _optional = hasOptional ? new InternalPoint { Value = 30 } : null;
#else
[DurableType("HPair", 2)] internal partial struct CurrentPair<T> {
    [DurableField(1)] public T Value;
    [DurableField(2)] public long Stamp;
}
[DurableType("HPoint", 2)] internal partial struct CurrentPoint {
    [DurableField(1)] public long Value;
}
[DurableType("HBase", 2)] public abstract partial class CurrentBase<T> : IDurableObject where T : struct {
    [DurableField(1)] private readonly CurrentPair<T> _pair;
    [DurableField(2)] private readonly CurrentPoint? _optional;
    [DurableField(4)] public CurrentBase<T>? Link;
    protected CurrentBase(T value, bool hasOptional) {
        _pair = new() { Value = value, Stamp = 20 };
        _optional = hasOptional ? new CurrentPoint { Value = 30 } : null;
#endif
        _ancestor = 40;
        _token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        BaseCatalog.ConstructorCalls++;
    }
    [DurableField(3)] private int _ancestor;
    [DurableField(5)] private readonly Guid _token;
    public T PairValue => _pair.Value;
    public long Stamp => _pair.Stamp;
    public long? Optional => _optional?.Value;
    public int Ancestor { get => _ancestor; set => _ancestor = value; }
    public bool TokenValid => _token == Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
}

public static class BaseCatalog {
    public static int ConstructorCalls;
    public static int UpgradeCalls;
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static bool LegacyClrAbsent => typeof(BaseCatalog).Assembly.GetType("InheritanceLibrary.Base.LegacyBase`1") is null &&
        typeof(BaseCatalog).Assembly.GetType("InheritanceLibrary.Base.InternalPair`1") is null &&
        typeof(BaseCatalog).Assembly.GetType("InheritanceLibrary.Base.InternalPoint") is null;
#if HISTORY_V2
    // An abstract base is never an independently stored object in this graph. Its owner rule
    // must not run when a leaf upgrades, even though the rule is explicitly registered.
    [DurableUpgrade(typeof(CurrentBase<>), 1)]
    public static void Upgrade<TState>(in BaseStates.V1<PairStates.V1<TState>, NullableState<PointStates.V1>> old,
        out BaseStates.V2<PairStates.V2<TState>, NullableState<PointStates.V2>> next, UpgradeContext context) where TState : unmanaged {
        UpgradeCalls++;
        next = new(new(old.Segment0Field1.Segment0Field1, old.Segment0Field1.Segment0Field2 + 1000L),
            old.Segment0Field2.HasValue ? new(new(old.Segment0Field2.Value.Segment0Field1 + 1000L)) : default,
            old.Segment0Field3, old.Segment0Field4, old.Segment0Field5);
    }
#endif
}
