using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

public delegate TState StateBaseReader<TState>(ref BinaryPayloadReader reader) where TState : unmanaged;
public delegate TState StateDeltaApplier<TState>(ref BinaryPayloadReader reader, in TState prior) where TState : unmanaged;

/// <summary>Reconstructs one exact-version DTO using explicitly selected static body functions.</summary>
/// <remarks>
/// Storage validates prior addresses. The Base selects the Schema for every Delta; the caller
/// supplies a matching reader and owns repository correspondence. This does not discover CLR
/// types, upgrade DTOs, resolve reference slots, or restore a domain graph.
/// </remarks>
public static class TypedObjectVersionReader {
    public static TState ReadDurable<TState>(
        ObjectVersionChain chain,
        SchemaStore schemas,
        DurableSchema expectedSchema,
        StateBaseReader<TState> readBase,
        StateDeltaApplier<TState> applyDelta) where TState : unmanaged {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(expectedSchema);
        ArgumentNullException.ThrowIfNull(readBase);
        ArgumentNullException.ThrowIfNull(applyDelta);
        ValidateShape(chain);
        BaseObjectPayload payload = BaseObjectPayloadCodec.Decode(chain.Records[0].Record.Body);
        if (payload.Kind != CapturedObjectKind.Durable || payload.SchemaKey is not SchemaKey key) {
            throw new InvalidDataException("A durable reader requires a durable Base type header.");
        }
        DurableSchema storedSchema = schemas.GetRequired(key);
        if (!storedSchema.Equals(expectedSchema)) {
            throw new InvalidDataException("The stored exact Schema definition does not match the selected body reader.");
        }

        BinaryPayloadReader reader = new(payload.Body);
        TState state = readBase(ref reader);
        reader.EnsureFullyConsumed();
        for (int index = 1; index < chain.Records.Count; index++) {
            reader = new(chain.Records[index].Record.Body);
            state = applyDelta(ref reader, in state);
            reader.EnsureFullyConsumed();
        }
        return state;
    }

    /// <summary>
    /// Reads built-in string content without SchemaStore. Use a StringReadTable to retain
    /// reference identity across ObjectIds; each independent nonempty decode creates content.
    /// </summary>
    public static string ReadString(ObjectVersionChain chain) {
        ValidateShape(chain);
        if (chain.Records.Count != 1) {
            throw new InvalidDataException("An immutable string object cannot have Delta records.");
        }
        BaseObjectPayload payload = BaseObjectPayloadCodec.Decode(chain.Records[0].Record.Body);
        if (payload.Kind != CapturedObjectKind.String) {
            throw new InvalidDataException("A string reader requires a string Base type header.");
        }
        BinaryPayloadReader reader = new(payload.Body);
        string value = reader.ReadString();
        reader.EnsureFullyConsumed();
        return value;
    }

    private static void ValidateShape(ObjectVersionChain chain) {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Records.Count == 0) {
            throw new InvalidDataException("An object version chain requires a Base.");
        }
        for (int index = 0; index < chain.Records.Count; index++) {
            ObjectVersionRecord record = chain.Records[index].Record;
            if (record.ObjectId != chain.ObjectId ||
                record.Kind != (index == 0 ? ObjectVersionKind.Base : ObjectVersionKind.Delta)) {
                throw new InvalidDataException("An object version chain must contain one Base followed by this object's Deltas.");
            }
        }
    }
}
