namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>The representation of one opaque object version, independent of the head-map kind.</summary>
public enum ObjectVersionKind : byte {
    Base = 1,
    Delta = 2,
}
