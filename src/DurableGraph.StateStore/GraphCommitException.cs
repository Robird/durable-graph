using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>The publication outcome of an interrupted Commit, independently of domain mutations.</summary>
public enum GraphCommitOutcome { NotPublished, Unknown, Published }

/// <summary>A save failure with an explicit publication outcome and, when known, candidate address.</summary>
/// <remarks>Unknown or Published outcomes must not be transparently retried. Dispose and reopen a faulted repository.</remarks>
public sealed class GraphCommitException : IOException {
    internal GraphCommitException(GraphCommitOutcome outcome, FrameAddress? candidate, Exception inner)
        : base($"Graph Commit interrupted; publication outcome: {outcome}.", inner) {
        Outcome = outcome;
        CandidateRevisionAddress = candidate;
    }

    public GraphCommitOutcome Outcome { get; }
    public FrameAddress? CandidateRevisionAddress { get; }
}

// Internal deterministic fault injection; never part of the host publication contract.
internal enum CommitCheckpoint { AfterPrepare, BeforeStateAppend, AfterStateDurable, BeforePublication, AfterPublicationAppend, AfterPublication }
