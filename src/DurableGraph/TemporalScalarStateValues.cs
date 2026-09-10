using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Static DateOnly operations for its complete day-number representation.</summary>
public readonly struct DateOnlyStateOps : IStateOps<DateOnly> {
    public static bool StateEquals(in DateOnly left, in DateOnly right, DurableFieldInfo slot) => left == right;
    public static void WriteBase(ref BinaryPayloadWriter writer, in DateOnly state, DurableFieldInfo slot) => writer.WriteDateOnly(state);
    public static DateOnly ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadDateOnly();
    public static PreparedDeltaBody PrepareDelta(in DateOnly prior, in DateOnly current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<DateOnly, DateOnlyStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static DateOnly ApplyDelta(ref BinaryPayloadReader reader, in DateOnly prior, DurableFieldInfo slot) {
        DateOnly current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in DateOnly state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static TimeOnly operations for its complete within-day tick representation.</summary>
public readonly struct TimeOnlyStateOps : IStateOps<TimeOnly> {
    public static bool StateEquals(in TimeOnly left, in TimeOnly right, DurableFieldInfo slot) => left == right;
    public static void WriteBase(ref BinaryPayloadWriter writer, in TimeOnly state, DurableFieldInfo slot) => writer.WriteTimeOnly(state);
    public static TimeOnly ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadTimeOnly();
    public static PreparedDeltaBody PrepareDelta(in TimeOnly prior, in TimeOnly current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<TimeOnly, TimeOnlyStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static TimeOnly ApplyDelta(ref BinaryPayloadReader reader, in TimeOnly prior, DurableFieldInfo slot) {
        TimeOnly current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in TimeOnly state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static DateTimeOffset operations preserving both clock ticks and UTC offset.</summary>
public readonly struct DateTimeOffsetStateOps : IStateOps<DateTimeOffset> {
    public static bool StateEquals(in DateTimeOffset left, in DateTimeOffset right, DurableFieldInfo slot) => left.EqualsExact(right);
    public static void WriteBase(ref BinaryPayloadWriter writer, in DateTimeOffset state, DurableFieldInfo slot) => writer.WriteDateTimeOffset(state);
    public static DateTimeOffset ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadDateTimeOffset();
    public static PreparedDeltaBody PrepareDelta(in DateTimeOffset prior, in DateTimeOffset current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<DateTimeOffset, DateTimeOffsetStateOps>(in current, slot, StateEquals(in prior, in current, slot));
    public static DateTimeOffset ApplyDelta(ref BinaryPayloadReader reader, in DateTimeOffset prior, DurableFieldInfo slot) {
        DateTimeOffset current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(StateEquals(in prior, in current, slot));
        return current;
    }
    public static void VisitReferences(in DateTimeOffset state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}
