using Atelia.DurableGraph;

namespace Atelia.ListDeltaReplayProbe;

[DurableType("replay.world", 1)]
public partial class World<T> : DurableBase {
    [DurableField(1)] public List<T> Items = [];
    [DurableField(2)] public List<T> Alias = [];
    [DurableField(3)] public Node[] Pool = [];
}

[DurableType("replay.node", 1)]
public partial class Node : DurableBase {
    [DurableField(1)] public int TestId;
    [DurableField(2)] public int Value;
    [DurableField(3)] public Node? Next;
}

[DurableType("replay.cell", 1)]
public partial struct Cell<T> {
    [DurableField(1)] public int TestId;
    [DurableField(2)] public T Value;
}

[DurableType("replay.wide", 1)]
public partial struct Wide<T> {
    [DurableField(1)] public int TestId;
    [DurableField(2)] public T Value;
    [DurableField(3)] public long A;
    [DurableField(4)] public long B;
    [DurableField(5)] public long C;
    [DurableField(6)] public long D;
    [DurableField(7)] public long E;
    [DurableField(8)] public long F;
    [DurableField(9)] public long G;
    [DurableField(10)] public long H;
}

// A minimal independent event makes the replay use the public E/S publication contract.
// It deliberately never points at World, so event capture does not rerun List diff.
[DurableType("replay.event", 1)]
public partial class ReplayEvent : DurableBase {
    [DurableField(1)] public byte Marker;
}
