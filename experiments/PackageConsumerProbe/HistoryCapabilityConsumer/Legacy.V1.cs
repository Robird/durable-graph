using Atelia.DurableGraph;

namespace HistoryCapabilityPackageConsumerProbe;

[DurableType("package.history-legacy", 1)]
public sealed partial class Legacy : DurableBase {
    [DurableField(1)] private int _value;
    [DurableField(2)] private Legacy? _next;

    internal Legacy(int value) { _value = value; _next = this; }
    internal void Change(int value) { _value = value; }
    internal bool HasSelfCycle => ReferenceEquals(this, _next);
}
