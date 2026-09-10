using Atelia.DurableGraph;
#if HISTORY_V1
using Leaf = InlineLibrary.B.LegacyLeaf;
#else
using Leaf = InlineLibrary.B.CurrentLeaf;
#endif

namespace InlineLibrary.B;

// Public values may contain private implementation values that are not public CLR APIs.
#if HISTORY_V1
[DurableType("IBLeaf", 1)]
internal partial struct LegacyLeaf {
    [DurableField(1)] public int Value;
}
[DurableType("IBPoint", 1)]
public partial struct LegacyPoint {
#else
[DurableType("IBLeaf", 2)]
internal partial struct CurrentLeaf {
    [DurableField(1)] public long Value;
}
[DurableType("IBPoint", 2)]
public partial struct CurrentPoint {
#endif
    [DurableField(1)] private Leaf _value;
    public long Value {
        get => _value.Value;
        set {
#if HISTORY_V1
            _value.Value = checked((int)value);
#else
            _value.Value = value;
#endif
        }
    }
}

public static class LibraryBCatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static bool LegacyClrAbsent => typeof(LibraryBCatalog).Assembly.GetType("InlineLibrary.B.LegacyPoint") is null &&
        typeof(LibraryBCatalog).Assembly.GetType("InlineLibrary.B.LegacyLeaf") is null;
}
