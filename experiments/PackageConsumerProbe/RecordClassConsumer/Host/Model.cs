using Atelia.DurableGraph;
using RecordClassLibrary.Facts;

namespace RecordClassConsumer;

[DurableType("RecordWorld", 1)]
public sealed partial class World : IDurableObject {
    [DurableField(1)] public int Hp = 10;
    [DurableField(2)] public Damage<string> First;
    [DurableField(3)] public Damage<string> Second;
    [DurableField(4)] public Damage<string> Alias;
    [DurableField(5)] public Damage<string>? Last;
    public World() {
        First = new("hero", 3);
        Second = new("hero", 3);
        Alias = First;
    }
}
