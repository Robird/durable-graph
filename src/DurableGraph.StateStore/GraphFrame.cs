using Atelia.EventJournal;
using StateFrameAddress = Atelia.DurableGraph.StateStore.Storage.FrameAddress;

namespace Atelia.DurableGraph.StateStore;

/// <summary>The role of a graph in an EventHistory logical chain.</summary>
public enum GraphFrameKind : uint {
    Event = 1,
    State = 2,
}

/// <summary>A graph location issued by, and valid only within, one opened repository.</summary>
/// <remarks>Its revision address is diagnostic. Use the handle itself for history operations.</remarks>
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
    public GraphFrameKind Kind { get; }
    public StateFrameAddress RevisionAddress { get; }
    public ObjectId RootId { get; }
}

internal sealed record HistoryGraphRecord(
    EventAddress Address,
    EventAddress? Parent,
    GraphFrameKind Kind,
    StateFrameAddress RevisionAddress,
    ObjectId RootId);
