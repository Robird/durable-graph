namespace Atelia.DurableGraph.StateStore.Storage;

// Cumulative traversal work, independent of decoded-frame/map cache residency and I/O.
// Reads count argument-valid calls, including calls that later fail data validation.
// Entries count records added to a reconstruction chain; replay frames count each
// materializer read attempt, including decoded-frame cache hits and failed reads.
internal readonly record struct StateRevisionTraversalStatistics(
    long ObjectChainReads,
    long ObjectChainEntries,
    long MapReplayFrames);
