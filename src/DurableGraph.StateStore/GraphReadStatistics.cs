namespace Atelia.DurableGraph.StateStore;

/// <summary>Operation-local evidence counters; they do not measure physical Frame I/O.</summary>
internal sealed class GraphReadStatistics {
    internal int DecodedObjects { get; set; }
    internal int CacheHits { get; set; }
    // Allocate calls include strings, and do not equal newly allocated CLR heap objects.
    internal int AllocatedObjects { get; set; }
    internal int HydratedObjects { get; set; }
    internal int SharedObjects { get; set; }
}
