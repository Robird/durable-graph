using Atelia.DurableGraph;
using InheritanceLibrary.Base;
using MiddleStates = Atelia.DurableGraph.Generated.Family_484D6964646C65;
using PairStates = Atelia.DurableGraph.Generated.Family_4850616972;
using PointStates = Atelia.DurableGraph.Generated.Family_48506F696E74;

namespace InheritanceLibrary.Middle;

#if HISTORY_V1
[DurableType("HMiddle", 1)] public abstract partial class Middle<T> : LegacyBase<T> where T : struct {
#else
[DurableType("HMiddle", 2)] public abstract partial class Middle<T> : CurrentBase<T> where T : struct {
#endif
    [DurableField(1)] private readonly int _middle;
    [DurableField(2)] private readonly T _extra;
    protected Middle(T value, bool hasOptional) : base(value, hasOptional) {
        _middle = 60;
        _extra = value;
        MiddleCatalog.ConstructorCalls++;
    }
    public int MiddleValue => _middle;
    public T Extra => _extra;
}

public static class MiddleCatalog {
    public static int ConstructorCalls;
    public static int UpgradeCalls;
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
#if HISTORY_V2
    [DurableUpgrade(typeof(Middle<>), 1)]
    public static void Upgrade<TState>(in MiddleStates.V1<PairStates.V1<TState>, NullableState<PointStates.V1>, TState> old,
        out MiddleStates.V2<PairStates.V2<TState>, NullableState<PointStates.V2>, TState> next, UpgradeContext context) where TState : unmanaged {
        UpgradeCalls++;
        next = new(new(old.Segment0Field1.Segment0Field1, old.Segment0Field1.Segment0Field2 + 1000L),
            old.Segment0Field2.HasValue ? new(new(old.Segment0Field2.Value.Segment0Field1 + 1000L)) : default,
            old.Segment0Field3, old.Segment0Field4, old.Segment0Field5, old.Segment1Field1, old.Segment1Field2);
    }
#endif
}
