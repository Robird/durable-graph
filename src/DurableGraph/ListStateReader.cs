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
    private const byte Copy = 1;
    private const byte New = 2;
    private const byte CopyAndPatch = 3;

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

    internal static PreparedDeltaBody PrepareDelta(FrozenListState<TState> prior, FrozenListState<TState> current,
        ListLayout layout, ListDeltaAlgorithm algorithm = ListDeltaAlgorithm.LocalResync) {
        if (algorithm is not (ListDeltaAlgorithm.Position or ListDeltaAlgorithm.LocalResync or ListDeltaAlgorithm.BoundedMyers)) {
            throw new ArgumentOutOfRangeException(nameof(algorithm));
        }
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteUInt32((uint)current.Count);
        bool changed = prior.Count != current.Count;
        if (!changed) {
            for (int index = 0; index < current.Count; index++) {
                if (!TOps.StateEquals(in prior.OwnedElements[index], in current.OwnedElements[index], layout.ElementSlot)) {
                    changed = true;
                    break;
                }
            }
        }
        // NoChange is a property of the two sequences, not of the matcher or its search budget.
        if (!changed) {
            if (current.Count > 0) { WriteSourceRange(ref writer, Copy, 0, current.Count); }
            return new(false, buffer.WrittenSpan);
        }

        List<ListDeltaRange> ranges = ListDeltaMatcher<TState, TOps>.Plan(prior.Elements, current.Elements, layout.ElementSlot, algorithm);
        foreach (ListDeltaRange range in ranges) {
            if (range.OldStart < 0) {
                writer.WriteByte(New);
                writer.WriteUInt32((uint)range.Count);
                for (int index = 0; index < range.Count; index++) {
                    TOps.WriteBase(ref writer, in current.OwnedElements[range.NewStart + index], layout.ElementSlot);
                }
                continue;
            }
            ArrayBufferWriter<byte>? patches = null;
            for (int index = 0; index < range.Count; index++) {
                ref readonly TState oldValue = ref prior.OwnedElements[range.OldStart + index];
                ref readonly TState newValue = ref current.OwnedElements[range.NewStart + index];
                if (TOps.StateEquals(in oldValue, in newValue, layout.ElementSlot)) { continue; }
                PreparedDeltaBody delta = TOps.PrepareDelta(in oldValue, in newValue, layout.ElementSlot);
                if (!delta.HasChanges) {
                    throw new InvalidOperationException("List element StateEquals and PrepareDelta disagree about a changed value.");
                }
                patches ??= new();
                BinaryPayloadWriter patchWriter = new(patches);
                patchWriter.WriteUInt32((uint)index + 1);
                patchWriter.WriteSpan(delta.Body);
            }
            WriteSourceRange(ref writer, patches is null ? Copy : CopyAndPatch, range.OldStart, range.Count);
            if (patches is not null) {
                writer.WriteSpan(patches.WrittenSpan);
                writer.WriteUInt32(0);
            }
        }
        // TODO: Consider local New-versus-Patch costs only if replay evidence justifies extra encoding.
        return new(true, buffer.WrittenSpan);
    }

    internal static FrozenListState<TState> ApplyDelta(ref BinaryPayloadReader reader, FrozenListState<TState> prior, ListLayout layout) {
        int count = ReadCount(ref reader);
        TState[]? elements = null;
        int output = 0;
        while (output < count) {
            byte operation = reader.ReadByte();
            if (operation == New) {
                int length = ReadRangeLength(ref reader, count - output);
                // Source copies can represent many elements in a few bytes; only literals have this lower bound.
                RequireTailFits(length, ref reader, layout);
                elements ??= new TState[count];
                for (int index = 0; index < length; index++) {
                    elements[output + index] = TOps.ReadBase(ref reader, layout.ElementSlot);
                }
                output += length;
                continue;
            }
            if (operation is not (Copy or CopyAndPatch)) {
                throw new InvalidDataException("Unknown List Delta operation.");
            }
            uint source = reader.ReadUInt32();
            int rangeLength = ReadRangeLength(ref reader, count - output);
            if (source > (uint)prior.Count || rangeLength > prior.Count - (int)source) {
                throw new InvalidDataException("List Delta source range is outside the prior state.");
            }
            elements ??= new TState[count];
            prior.Elements.Slice((int)source, rangeLength).CopyTo(elements.AsSpan(output, rangeLength));
            if (operation == CopyAndPatch) {
                uint previous = 0;
                while (true) {
                    uint encoded = reader.ReadUInt32();
                    if (encoded == 0) {
                        if (previous == 0) { throw new InvalidDataException("A List CopyAndPatch operation must change an element."); }
                        break;
                    }
                    if (encoded <= previous || encoded > rangeLength) {
                        throw new InvalidDataException("List patch indexes must be strictly increasing and within their source range.");
                    }
                    int index = (int)encoded - 1;
                    elements[output + index] = TOps.ApplyDelta(ref reader, in prior.OwnedElements[(int)source + index], layout.ElementSlot);
                    previous = encoded;
                }
            }
            // A final CopyAndPatch still consumes its terminating zero before completing the output.
            output += rangeLength;
        }
        return new(elements ?? [], takeOwnership: true);
    }

    private static int ReadRangeLength(ref BinaryPayloadReader reader, int remaining) {
        uint length = reader.ReadUInt32();
        if (length == 0 || length > remaining) {
            throw new InvalidDataException("List Delta range length must be positive and fit the remaining output.");
        }
        return (int)length;
    }

    private static void WriteSourceRange(ref BinaryPayloadWriter writer, byte operation, int start, int count) {
        writer.WriteByte(operation);
        writer.WriteUInt32((uint)start);
        writer.WriteUInt32((uint)count);
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
