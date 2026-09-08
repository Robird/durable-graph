using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Encodes the versioned type header carried by Base records only.</summary>
/// <remarks>
/// Version 2 uses tag 1 for string and tag 2 followed by a closed type expression and version.
/// Version 1 is read as the corresponding zero-argument named Schema key.
/// Remaining bytes are the raw body; this codec neither validates that body nor registers Schema.
/// Delta bodies have no header and inherit the Base's exact interpretation.
/// </remarks>
internal static class BaseObjectBodyCodec {
    internal static EncodedBaseObjectBody EncodeString(PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(rawBody);
        return Encode(rawBody, null);
    }

    /// <summary>Wraps a raw body. The caller must register its Schema before saving the object.</summary>
    internal static EncodedBaseObjectBody EncodeDurable(DurableSchema schema, PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Kind != SchemaKind.ReferenceObject) {
            throw new ArgumentException("Only reference-object Schemas may identify an object Base.", nameof(schema));
        }
        ArgumentNullException.ThrowIfNull(rawBody);
        return Encode(rawBody, new SchemaKey(schema.Type, schema.Version));
    }

    internal static DecodedBaseObjectBody Decode(ReadOnlySpan<byte> encodedBody) {
        BinaryPayloadReader reader = new(encodedBody);
        byte version = reader.ReadByte();
        if (version is not 1 and not 2) {
            throw new InvalidDataException($"Unsupported Base type header version {version}.");
        }

        byte tag = reader.ReadByte();
        ObjectStateKind kind;
        SchemaKey? key;
        switch (tag) {
            case 1:
                kind = ObjectStateKind.String;
                key = null;
                break;
            case 2:
                kind = ObjectStateKind.Durable;
                key = version == 1 ? SchemaKeyWireCodec.ReadLegacy(ref reader) : SchemaKeyWireCodec.Read(ref reader);
                break;
            default:
                throw new InvalidDataException($"Unsupported Base type tag {tag}.");
        }

        return new(kind, key, encodedBody[reader.ConsumedCount..]);
    }

    private static EncodedBaseObjectBody Encode(PreparedBaseBody rawBody, SchemaKey? key) {
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteByte(2);
        writer.WriteByte(key.HasValue ? (byte)2 : (byte)1);
        if (key is SchemaKey exact) {
            SchemaKeyWireCodec.Write(ref writer, exact);
        }
        buffer.Write(rawBody.Body);
        // TODO: After MVP, avoid the intermediate buffer/copy when wrapping prepared content.
        return new(buffer.WrittenSpan);
    }
}
