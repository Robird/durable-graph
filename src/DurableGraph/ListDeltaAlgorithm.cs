namespace Atelia.DurableGraph;

/// <summary>Selects a List Delta writer without changing its persistent representation.</summary>
public enum ListDeltaAlgorithm {
    Position = 0,
    LocalResync = 1,
    BoundedMyers = 2,
    /// <summary>Keeps a complete Local result and accepts a triggered, bounded Myers candidate only when its body is smaller.</summary>
    Adaptive = 3,
}
