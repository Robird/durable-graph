namespace Atelia.DurableGraph;

/// <summary>Selects a List writer's matching algorithm without changing its persistent representation.</summary>
public enum ListDeltaAlgorithm {
    Position,
    LocalResync,
    BoundedMyers,
}
