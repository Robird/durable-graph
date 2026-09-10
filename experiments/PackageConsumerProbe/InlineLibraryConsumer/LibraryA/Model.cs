using Atelia.DurableGraph;
#if HISTORY_V1
using Point = InlineLibrary.B.LegacyPoint;
#else
using Point = InlineLibrary.B.CurrentPoint;
#endif

namespace InlineLibrary.A;

#if HISTORY_V1
[DurableType("IACoordinate", 1)]
#else
[DurableType("IACoordinate", 2)]
#endif
public partial struct Coordinate {
    [DurableField(1)] public Point Point;
    [DurableField(2)] public int Region;
}

[DurableType("IAPair", 1)]
public partial struct Pair<T> where T : struct {
    [DurableField(1)] public T Value;
    [DurableField(2)] public int Weight;
}

public static class LibraryACatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
}
