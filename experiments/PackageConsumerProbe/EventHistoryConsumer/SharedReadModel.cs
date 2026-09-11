using Atelia.DurableGraph;

namespace EventHistoryPackageConsumerProbe;

// These five schemas stay unchanged in both builds. The separate repository below
// exercises same-layout sharing without changing the Alice/World Upgrade witness.
[DurableType("SharedNode", 1)]
public sealed partial class SharedNode : DurableBase {
    [DurableField(1)] public int Value;
    [DurableField(2)] public SharedNode? Next;
    [DurableField(3)] public string Text = "";
    // Application-only context, initialized only on independent reads in this probe.
    [Transient] public SharedRoot? OwnerWorld;
}

[DurableType("SharedLinks", 1)]
public partial struct SharedLinks {
    [DurableField(1)] public SharedNode? Target;
}

[DurableType("SharedHolder", 1)]
public sealed partial class SharedHolder<T> : DurableBase {
    [DurableField(1)] public T Value = default!;
}

[DurableType("SharedRoot", 1)]
public sealed partial class SharedRoot : DurableBase {
    [DurableField(1)] public SharedNode Stable = null!;
    [DurableField(2)] public SharedNode Changing = null!;
    [DurableField(3)] public SharedNode[] StableArray = [];
    [DurableField(4)] public List<SharedNode> StableList = [];
    [DurableField(5)] public Dictionary<string, SharedNode> StableMap = [];
    [DurableField(6)] public SharedLinks StableInline;
    [DurableField(7)] public SharedLinks? StableOptional;
    [DurableField(8)] public SharedNode[] ChangingArray = [];
    [DurableField(9)] public List<SharedNode> ChangingList = [];
    [DurableField(10)] public Dictionary<string, SharedNode> ChangingMap = [];
    [DurableField(11)] public SharedLinks ChangingInline;
    [DurableField(12)] public SharedLinks? ChangingOptional;
    [DurableField(13)] public string EqualOne = "";
    [DurableField(14)] public string EqualTwo = "";
    [DurableField(15)] public Dictionary<SharedNode, SharedNode> ChangingKeyMap = new(ReferenceEqualityComparer.Instance);
    [DurableField(16)] public SharedHolder<SharedLinks> InlineOwner = null!;
    [DurableField(17)] public SharedHolder<SharedLinks?> OptionalOwner = null!;
}

[DurableType("SharedEvent", 1)]
public sealed partial class SharedEvent : DurableBase {
    [DurableField(1)] public SharedRoot View = null!;
}
