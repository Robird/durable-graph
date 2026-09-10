using Atelia.DurableGraph;
using InheritanceLibrary.Middle;
using LeafStates = Atelia.DurableGraph.Generated.Family_484C656166;
using PairStates = Atelia.DurableGraph.Generated.Family_4850616972;
using PointStates = Atelia.DurableGraph.Generated.Family_48506F696E74;
#if HISTORY_V1
using BaseInt = InheritanceLibrary.Base.LegacyBase<int>;
#else
using BaseInt = InheritanceLibrary.Base.CurrentBase<int>;
#endif

namespace InheritanceLibrary.App;

#if HISTORY_V1
[DurableType("HLeaf", 1)]
#else
[DurableType("HLeaf", 2)]
#endif
public partial class Leaf : Middle<int> {
    [DurableField(1)] public int LeafValue;
    [DurableField(2)] public BaseInt? Alias;
    [DurableField(3)] public List<BaseInt>? Peers;
    public Leaf(int value, bool hasOptional) : base(value, hasOptional) {
        LeafValue = 50;
        AppCatalog.ConstructorCalls++;
    }
}

public static class AppCatalog {
    public static int ConstructorCalls;
    public static int UpgradeCalls;
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static ObjectId HistoricalLink(ObjectStateRecord record) => record.GetState<LeafStates.V1<PairStates.V1<int>, NullableState<PointStates.V1>>>().Segment0Field4;
    public static void CheckHistorical(ObjectStateRecord record, int value, int ancestor, int leaf, bool hasOptional) {
        var old = record.GetState<LeafStates.V1<PairStates.V1<int>, NullableState<PointStates.V1>>>();
        if (old.Segment0Field1.Segment0Field1 != value || old.Segment0Field1.Segment0Field2 != 20 ||
            old.Segment0Field2.HasValue != hasOptional || (hasOptional && old.Segment0Field2.Value.Segment0Field1 != 30) ||
            old.Segment0Field3 != ancestor || old.Segment1Field1 != 60 || old.Segment1Field2 != value || old.Segment2Field1 != leaf) {
            throw new InvalidOperationException("Stored exact inheritance/hidden inline DTO changed.");
        }
    }
#if HISTORY_V2
    [DurableUpgrade(typeof(Leaf), 1)]
    public static void Upgrade(in LeafStates.V1<PairStates.V1<int>, NullableState<PointStates.V1>> old,
        out LeafStates.V2<PairStates.V2<int>, NullableState<PointStates.V2>> next, UpgradeContext context) {
        UpgradeCalls++;
        // The leaf controls conversion of its entire flattened DTO. Base/Middle owner edges
        // are deliberately not invoked, so this +1000 transformation is applied once.
        next = new(new(old.Segment0Field1.Segment0Field1, old.Segment0Field1.Segment0Field2 + 1000L),
            old.Segment0Field2.HasValue ? new(new(old.Segment0Field2.Value.Segment0Field1 + 1000L)) : default,
            old.Segment0Field3, old.Segment0Field4, old.Segment0Field5, old.Segment1Field1, old.Segment1Field2,
            old.Segment2Field1, old.Segment2Field2, old.Segment2Field3);
    }
#endif
}
