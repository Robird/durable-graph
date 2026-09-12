using Atelia.EventJournal;
using StateFrameAddress = Atelia.DurableGraph.Storage.FrameAddress;

namespace Atelia.DurableGraph.Persistence;

/// <summary>The role of a graph in an EventHistory logical chain.</summary>
public enum GraphFrameKind : uint {
    Event = 1,
    State = 2,
}

/// <summary>A graph location issued by, and valid only within, one opened repository.</summary>
/// <remarks>
/// Pass this handle to history operations on the repository instance that issued it. Handles from
/// another instance are rejected, including after reopening the same directory. Neither this handle
/// nor its diagnostic revision address is a persistent bookmark that can be resolved after reopening.
/// </remarks>
public sealed class GraphFrame {
    internal GraphFrame(object owner, HistoryGraphRecord record) {
        Owner = owner;
        Address = record.Address;
        Parent = record.Parent;
        Kind = record.Kind;
        RevisionAddress = record.RevisionAddress;
        RootId = record.RootId;
    }

    internal object Owner { get; }
    internal EventAddress Address { get; }
    internal EventAddress? Parent { get; }
    /// <summary>The graph's State/Event role, independently of its domain root's CLR type.</summary>
    public GraphFrameKind Kind { get; }
    /// <summary>The underlying StateRevision address for diagnostics, not a cross-reopen bookmark.</summary>
    public StateFrameAddress RevisionAddress { get; }
    /// <summary>The root's object identity interpreted within this graph's StateRevision.</summary>
    public ObjectId RootId { get; }
}

internal sealed record HistoryGraphRecord(
    EventAddress Address,
    EventAddress? Parent,
    GraphFrameKind Kind,
    StateFrameAddress RevisionAddress,
    ObjectId RootId);
