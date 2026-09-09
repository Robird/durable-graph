using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Reads exact List contents without requiring the historical domain element CLR type.</summary>
public abstract class ListStateReader : ObjectReaderBinding {
    private protected ListStateReader(ListLayout layout) : base(ObjectLayout.ForList(layout)) { }

    public static ListStateReader Create(ListLayout layout, StateValueBinding element) {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(element);
        if (layout.ElementSlot != StateBindingContext.WithFieldId(element.Slot, 1)) {
            throw new ArgumentException("The stored element binding must match the complete List element layout.");
        }
        return (ListStateReader)Activator.CreateInstance(
            typeof(ListStateReader<,>).MakeGenericType(element.StateType, element.StateOpsType), layout)!;
    }
}

internal sealed class ListStateReader<TState, TOps> : ListStateReader
    where TState : unmanaged where TOps : IStateOps<TState> {
    public ListStateReader(ListLayout layout) : base(layout) { }
    public override Type StateType => typeof(FrozenListState<TState>);

    internal override ObjectStateRecord Read(ObjectId id, IStateBodySource source) {
        ArgumentNullException.ThrowIfNull(source);
        if (id.IsNull || source.Count <= 0) { throw new InvalidDataException("A List requires a nonzero ID and a Base body."); }
        BinaryPayloadReader reader = new(source.GetBody(0));
        FrozenListState<TState> state = ListStateBody<TState, TOps>.ReadBase(ref reader, Layout.List!);
        reader.EnsureFullyConsumed();
        for (int index = 1; index < source.Count; index++) {
            reader = new(source.GetBody(index));
            state = ListStateBody<TState, TOps>.ApplyDelta(ref reader, state, Layout.List!);
            reader.EnsureFullyConsumed();
        }
        return new(id, Layout.List!, state);
    }

    internal override void VisitReferences(ObjectStateRecord item, IStateReferenceVisitor visitor) {
        if (!Layout.Equals(item.Layout)) { throw new InvalidDataException("The List reader requires its exact layout."); }
        ListStateBody<TState, TOps>.VisitReferences(item.GetListState<TState>(), visitor, Layout.List!);
    }
}

internal static class ListStateBody<TState, TOps> where TState : unmanaged where TOps : IStateOps<TState> {
    internal static PreparedBaseBody PrepareBase(FrozenListState<TState> state, ListLayout layout) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt32((uint)state.Count);
        foreach (ref readonly TState element in state.Elements) { TOps.WriteBase(ref writer, in element, layout.ElementSlot); }
        return new(buffer.WrittenSpan);
    }

    internal static FrozenListState<TState> ReadBase(ref BinaryPayloadReader reader, ListLayout layout) {
        int count = ReadCount(ref reader);
        RequireTailFits(count, ref reader, layout);
        TState[] elements = new TState[count];
        for (int index = 0; index < elements.Length; index++) { elements[index] = TOps.ReadBase(ref reader, layout.ElementSlot); }
        return new(elements, takeOwnership: true);
    }

    // TODO: Position diffs amplify middle insert/delete edits. Select a compact edit algorithm in
    // docs/DurableGraph-research-roadmap.md section 3.2; a changed patch grammar requires a new codec version.
    internal static PreparedDeltaBody PrepareDelta(FrozenListState<TState> prior, FrozenListState<TState> current, ListLayout layout) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt32((uint)current.Count);
        bool changed = prior.Count != current.Count;
        int common = Math.Min(prior.Count, current.Count);
        for (int index = 0; index < common; index++) {
            PreparedDeltaBody delta = TOps.PrepareDelta(in prior.OwnedElements[index], in current.OwnedElements[index], layout.ElementSlot);
            if (!delta.HasChanges) { continue; }
            changed = true;
            writer.WriteUInt32((uint)index + 1);
            writer.WriteSpan(delta.Body);
        }
        writer.WriteUInt32(0);
        for (int index = common; index < current.Count; index++) { TOps.WriteBase(ref writer, in current.OwnedElements[index], layout.ElementSlot); }
        return new(changed, buffer.WrittenSpan);
    }

    internal static FrozenListState<TState> ApplyDelta(ref BinaryPayloadReader reader, FrozenListState<TState> prior, ListLayout layout) {
        int count = ReadCount(ref reader);
        int common = Math.Min(prior.Count, count);
        // Even before consuming prefix deltas, a nonempty encoded tail must fit the remaining bytes.
        RequireTailFits(count - common, ref reader, layout);
        TState[] elements = new TState[count];
        prior.Elements[..common].CopyTo(elements);
        uint previous = 0;
        while (true) {
            uint encoded = reader.ReadUInt32();
            if (encoded == 0) { break; }
            if (encoded <= previous || encoded > common) {
                throw new InvalidDataException("List Delta indexes must be strictly increasing and within the common prefix.");
            }
            int index = (int)encoded - 1;
            elements[index] = TOps.ApplyDelta(ref reader, in elements[index], layout.ElementSlot);
            previous = encoded;
        }
        RequireTailFits(count - common, ref reader, layout);
        for (int index = common; index < count; index++) { elements[index] = TOps.ReadBase(ref reader, layout.ElementSlot); }
        return new(elements, takeOwnership: true);
    }

    internal static void VisitReferences(FrozenListState<TState> state, IStateReferenceVisitor visitor, ListLayout layout) {
        foreach (ref readonly TState element in state.Elements) { TOps.VisitReferences(in element, visitor, layout.ElementSlot); }
    }

    private static int ReadCount(ref BinaryPayloadReader reader) {
        uint count = reader.ReadUInt32();
        if (count > Array.MaxLength) { throw new InvalidDataException("List count exceeds the supported CLR buffer length."); }
        return (int)count;
    }

    private static void RequireTailFits(int count, ref BinaryPayloadReader reader, ListLayout layout) {
        int minimum = StateBodySize.MinimumBaseBytes(layout.ElementSlot);
        if (minimum > 0 && count > reader.RemainingCount / minimum) {
            throw new InvalidDataException("List elements cannot fit in the remaining payload.");
        }
    }
}
