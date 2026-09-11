using Atelia.DurableGraph;
using InlineLibrary.A;
using WorldStates = Atelia.DurableGraph.Generated.Family_49576F726C64;
using PointStates = Atelia.DurableGraph.Generated.Family_4942506F696E74;
using PairStates = Atelia.DurableGraph.Generated.Family_494150616972;
#if HISTORY_V1
using Point = InlineLibrary.B.LegacyPoint;
#else
using Point = InlineLibrary.B.CurrentPoint;
#endif

namespace InlineLibrary.App;

#if HISTORY_V1
[DurableType("IWorld", 1)]
#else
[DurableType("IWorld", 2)]
#endif
public partial class World : IDurableObject {
    [DurableField(1)] public Coordinate Position;
    [DurableField(2)] public Point? Optional;
    [DurableField(3)] public Pair<int> Tag;
    [DurableField(4)] private readonly Guid _token;

    public long Value {
        get => Position.Point.Value;
        set => Position.Point.Value = value;
    }
    public World() {
        Position = new() { Point = new() { Value = 10 }, Region = 7 };
        Optional = new Point { Value = 20 };
        Tag = new() { Value = 30, Weight = 40 };
        _token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    }
    public bool IsValid(long value, long optional) => Value == value && Optional?.Value == optional &&
        Position.Region == 7 && Tag.Value == 30 && Tag.Weight == 40 &&
        _token == Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
}

public static class AppCatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static int UpgradeCalls { get; private set; }
    public static void CheckHistorical(ObjectStateRecord record, long expected) {
        var old = record.GetState<WorldStates.V1<NullableState<PointStates.V1>, PairStates.V1<int>>>();
        if (old.Segment0Field1.Segment0Field1.Segment0Field1.Segment0Field1 != expected ||
            old.Segment0Field1.Segment0Field2 != 7 || !old.Segment0Field2.HasValue ||
            old.Segment0Field2.Value.Segment0Field1.Segment0Field1 != 20 ||
            old.Segment0Field3.Segment0Field1 != 30 || old.Segment0Field3.Segment0Field2 != 40) {
            throw new InvalidOperationException("Stored-exact transitive inline DTO changed.");
        }
    }
#if HISTORY_V2
    [DurableUpgrade(typeof(World), 1)]
    internal static void Upgrade(in WorldStates.V1<NullableState<PointStates.V1>, PairStates.V1<int>> old,
        out WorldStates.V2<NullableState<PointStates.V2>, PairStates.V1<int>> next, UpgradeContext context) {
        UpgradeCalls++;
        next = new(
            new(new(new(old.Segment0Field1.Segment0Field1.Segment0Field1.Segment0Field1 + 1000L)), old.Segment0Field1.Segment0Field2),
            old.Segment0Field2.HasValue ? new(new(new(old.Segment0Field2.Value.Segment0Field1.Segment0Field1 + 1000L))) : default,
            old.Segment0Field3, old.Segment0Field4);
    }
#endif
}
