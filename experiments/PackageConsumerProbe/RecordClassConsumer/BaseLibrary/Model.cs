using Atelia.DurableGraph;

namespace RecordClassLibrary.Base;

#if HISTORY_V1
[DurableType("RecordFact", 1)]
public abstract partial class Fact<T> : IDurableObject {
    [DurableField(1)] public readonly T Actor;
    protected Fact(T actor) { Actor = actor; }
}
#else
// The schema is unchanged: the same FieldId and slot now use record backing storage.
[DurableType("RecordFact", 1)]
public abstract partial record Fact<T>([field: DurableField(1)] T Actor) : IDurableObject;
#endif

public static class BaseCatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
}
