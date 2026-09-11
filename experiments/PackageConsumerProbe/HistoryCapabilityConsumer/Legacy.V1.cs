using Atelia.DurableGraph;

namespace HistoryCapabilityPackageConsumerProbe;

[DurableType("package.history-legacy", 1)]
public sealed partial class Legacy : DurableBase {
    [DurableField(1)] private int _value;
    [DurableField(2)] private Legacy? _next;
    // A stable payload makes the score-only Delta smaller even after its prior locator.
    [DurableField(3)] private readonly ulong _createdAtTicks;

    internal Legacy(int value) { _value = value; _next = this; _createdAtTicks = 638_625_600_000_000_000; }
    internal Legacy SnapshotEvent() => new(_value);
    internal void Change(int value) { _value = value; }
    internal bool HasSelfCycle => ReferenceEquals(this, _next) && _createdAtTicks == 638_625_600_000_000_000;
}
