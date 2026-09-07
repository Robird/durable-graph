using Atelia.DurableGraph;

namespace StateStorePackageConsumerProbe;

// This first build publishes real package-generated history for the following V2 build.
[DurableType("package.restore-world", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public sealed partial class World : DurableBase {
    [DurableField(1)] private int _score;
    [DurableField(2)] private readonly string _name;

    public World(int score, string name) { _score = score; _name = name; }
}
