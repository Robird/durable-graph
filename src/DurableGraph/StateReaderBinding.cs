using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Supplies one unpacked Base body followed by raw Delta bodies in application order.</summary>
/// <remarks>The caller keeps the source and its buffers stable for the entire synchronous read.</remarks>
internal interface IStateBodySource {
    int Count { get; }
    ReadOnlySpan<byte> GetBody(int index);
}

public delegate TState StateBaseReader<TState>(ref BinaryPayloadReader reader) where TState : unmanaged;
public delegate TState StateDeltaApplier<TState>(ref BinaryPayloadReader reader, in TState prior) where TState : unmanaged;
public delegate void StateStringReferenceValidator<TState>(in TState state, StringReadTable table) where TState : unmanaged;

/// <summary>Receives generated readers for explicitly selected model families and their history.</summary>
public interface IStateReaderRegistration {
    void Register(StateReaderBinding reader);
}

/// <summary>One exact Schema and its typed body reader, erased only at the object boundary.</summary>
/// <remarks>Reading does not create a capture candidate or install a session baseline.</remarks>
public abstract class StateReaderBinding {
    private protected StateReaderBinding(DurableSchema schema) {
        ArgumentNullException.ThrowIfNull(schema);
        Schema = schema;
    }

    public DurableSchema Schema { get; }

    internal abstract CapturedObject Read(uint objectId, IStateBodySource source);
    internal abstract void ValidateReferences(CapturedObject item, StringReadTable strings);
}

/// <summary>Reconstructs one exact-version unmanaged DTO before boxing the completed value once.</summary>
/// <remarks>Callbacks read bodies or validate references; they must not publish partial loading state.</remarks>
public sealed class StateReaderBinding<TState> : StateReaderBinding where TState : unmanaged {
    private readonly StateBaseReader<TState> _readBase;
    private readonly StateDeltaApplier<TState> _applyDelta;
    private readonly StateStringReferenceValidator<TState> _validateReferences;

    public StateReaderBinding(
        DurableSchema schema,
        StateBaseReader<TState> readBase,
        StateDeltaApplier<TState> applyDelta,
        StateStringReferenceValidator<TState> validateReferences) : base(schema) {
        ArgumentNullException.ThrowIfNull(readBase);
        ArgumentNullException.ThrowIfNull(applyDelta);
        ArgumentNullException.ThrowIfNull(validateReferences);
        _readBase = readBase;
        _applyDelta = applyDelta;
        _validateReferences = validateReferences;
    }

    internal override CapturedObject Read(uint objectId, IStateBodySource source) {
        if (objectId == 0) {
            throw new InvalidDataException("A durable object ID must be nonzero.");
        }
        TState state = StateBodyDecoder.Read(source, _readBase, _applyDelta);
        return new CapturedObject(objectId, Schema, state);
    }

    internal override void ValidateReferences(CapturedObject item, StringReadTable strings) {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(strings);
        if (item.Kind != CapturedObjectKind.Durable || !Schema.Equals(item.Schema)) {
            throw new InvalidDataException("The object does not match this reader's exact Schema.");
        }
        TState state;
        try {
            state = item.GetState<TState>();
        } catch (InvalidOperationException error) {
            throw new InvalidDataException("The object does not contain this reader's exact DTO type.", error);
        }
        _validateReferences(in state, strings);
    }
}

internal static class StateBodyDecoder {
    internal static TState Read<TState>(
        IStateBodySource source,
        StateBaseReader<TState> readBase,
        StateDeltaApplier<TState> applyDelta) where TState : unmanaged {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(readBase);
        ArgumentNullException.ThrowIfNull(applyDelta);
        int count = source.Count;
        if (count <= 0) {
            throw new InvalidDataException("An object body source requires a Base.");
        }
        BinaryPayloadReader reader = new(source.GetBody(0));
        TState state = readBase(ref reader);
        reader.EnsureFullyConsumed();
        for (int index = 1; index < count; index++) {
            reader = new(source.GetBody(index));
            state = applyDelta(ref reader, in state);
            reader.EnsureFullyConsumed();
        }
        return state;
    }
}
