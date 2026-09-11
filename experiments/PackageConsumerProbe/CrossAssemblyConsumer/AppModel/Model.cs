using Atelia.DurableGraph;
using CrossAssembly.Domain;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;

namespace CrossAssembly.App;

[DurableType("World", 1)]
public partial class World : IDurableObject {
    [DurableField(1)] public Node Node = null!;
    [DurableField(2)] public Node Alias = null!;
    public static World Create() {
        Node node = CrossAssembly.Domain.Node.Create(10);
        return new World { Node = node, Alias = node };
    }
}

public static class AppCatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static ObjectId GetNodeId(ObjectStateRecord record) {
        var world = record.GetState<WorldStates.V1>();
        if (world.Segment0Field1 != world.Segment0Field2) throw new InvalidOperationException("Historical shared identity changed.");
        return world.Segment0Field1;
    }
}
