using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

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
        BaseObjectPayload payload = DecodeBase(chain);
        if (payload.Kind != CapturedObjectKind.Durable || payload.SchemaKey is not SchemaKey key) {
            throw new InvalidDataException("A durable reader requires a durable Base type header.");
        }
        MatchSchema(schemas, key, expectedSchema);
        return StateBodyDecoder.Read(CreateBodySource(chain, payload), readBase, applyDelta);
    }

    /// <summary>
    /// Reads built-in string content without SchemaStore. Use a StringReadTable to retain
    /// reference identity across ObjectIds; each independent nonempty decode creates content.
    /// </summary>
    public static string ReadString(ObjectVersionChain chain) {
        BaseObjectPayload payload = DecodeBase(chain);
        return ReadString(chain, payload);
    }

    internal static string ReadString(ObjectVersionChain chain, BaseObjectPayload payload) {
        if (chain.Records.Count != 1) {
            throw new InvalidDataException("An immutable string object cannot have Delta records.");
        }
        if (payload.Kind != CapturedObjectKind.String) {
            throw new InvalidDataException("A string reader requires a string Base type header.");
        }
        BinaryPayloadReader reader = new(payload.Body);
        string value = reader.ReadString();
        reader.EnsureFullyConsumed();
        return value;
    }

    internal static BaseObjectPayload DecodeBase(ObjectVersionChain chain) {
        ValidateShape(chain);
        return BaseObjectPayloadCodec.Decode(chain.Records[0].Record.Body);
    }

    internal static void MatchSchema(SchemaStore schemas, SchemaKey key, DurableSchema expectedSchema) {
        DurableSchema storedSchema = schemas.GetRequired(key);
        if (!storedSchema.Equals(expectedSchema)) {
            throw new InvalidDataException("The stored exact Schema definition does not match the selected body reader.");
        }
    }

    internal static IStateBodySource CreateBodySource(ObjectVersionChain chain, BaseObjectPayload payload) =>
        new ChainBodySource(chain, payload);

    // Borrow the owned chain for this synchronous read; Delta bodies do not need another copy.
    private sealed class ChainBodySource(ObjectVersionChain chain, BaseObjectPayload payload) : IStateBodySource {
        public int Count => chain.Records.Count;
        public ReadOnlySpan<byte> GetBody(int index) => index == 0 ? payload.Body : chain.Records[index].Record.Body;
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
