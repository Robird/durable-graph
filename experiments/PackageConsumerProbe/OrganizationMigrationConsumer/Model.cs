using Atelia.DurableGraph;

namespace OrganizationMigrationConsumer;

[DurableType("OrganizationMigrationPoint", 1)]
public partial struct Point {
    [DurableField(1)] public long X;
    [DurableField(2)] public long Y;
}

[DurableType("OrganizationMigrationWorld", 1)]
public partial class World : IDurableObject {
    [DurableField(1)] public long Value;
    [DurableField(2)] public World? Self;
    [DurableField(3)] public World? Alias;
    [DurableField(4)] public Point Position;
    [DurableField(5)] public long[] Numbers = [1, 2, 3];
    [DurableField(6)] public List<World> Links = [];
    [DurableField(7)] public Dictionary<string, World> Named = [];
    [DurableField(8)] public long A = 11;
    [DurableField(9)] public long B = 22;
    [DurableField(10)] public long C = 33;
    [DurableField(11)] public long D = 44;
}
