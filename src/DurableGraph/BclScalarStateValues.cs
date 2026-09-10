using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Static Guid operations for its complete built-in state representation.</summary>
public readonly struct GuidStateOps : IStateOps<Guid> {
    public static bool StateEquals(in Guid left, in Guid right, DurableFieldInfo slot) => left == right;
    public static void WriteBase(ref BinaryPayloadWriter writer, in Guid state, DurableFieldInfo slot) => writer.WriteGuid(state);
    public static Guid ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadGuid();
    public static PreparedDeltaBody PrepareDelta(in Guid prior, in Guid current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<Guid, GuidStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static Guid ApplyDelta(ref BinaryPayloadReader reader, in Guid prior, DurableFieldInfo slot) {
        Guid current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in Guid state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Decimal operations for its complete built-in state representation.</summary>
public readonly struct DecimalStateOps : IStateOps<decimal> {
    public static bool StateEquals(in decimal left, in decimal right, DurableFieldInfo slot) => ScalarStateEquality.DecimalEquals(in left, in right);
    public static void WriteBase(ref BinaryPayloadWriter writer, in decimal state, DurableFieldInfo slot) => writer.WriteDecimal(state);
    public static decimal ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadDecimal();
    public static PreparedDeltaBody PrepareDelta(in decimal prior, in decimal current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<decimal, DecimalStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static decimal ApplyDelta(ref BinaryPayloadReader reader, in decimal prior, DurableFieldInfo slot) {
        decimal current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in decimal state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static TimeSpan operations for its complete built-in state representation.</summary>
public readonly struct TimeSpanStateOps : IStateOps<TimeSpan> {
    public static bool StateEquals(in TimeSpan left, in TimeSpan right, DurableFieldInfo slot) => left.Ticks == right.Ticks;
    public static void WriteBase(ref BinaryPayloadWriter writer, in TimeSpan state, DurableFieldInfo slot) => writer.WriteTimeSpan(state);
    public static TimeSpan ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadTimeSpan();
    public static PreparedDeltaBody PrepareDelta(in TimeSpan prior, in TimeSpan current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<TimeSpan, TimeSpanStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static TimeSpan ApplyDelta(ref BinaryPayloadReader reader, in TimeSpan prior, DurableFieldInfo slot) {
        TimeSpan current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in TimeSpan state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}
