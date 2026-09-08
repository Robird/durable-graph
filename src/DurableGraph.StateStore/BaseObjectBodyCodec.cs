using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Encodes the persistent representation ID carried by Base records only.</summary>
/// <remarks>
/// New writes use version 4 followed by a canonical UInt32 representation ID and the raw body.
/// Versions 1–3 are read-only formats whose descriptors resolve through the same layout boundary.
/// This codec neither validates the raw body nor registers representations. Delta bodies have
/// no header and inherit the Base's exact interpretation.
/// </remarks>
internal static class BaseObjectBodyCodec {
    internal static EncodedBaseObjectBody EncodeString(PreparedBaseBody rawBody) =>
        Encode(RepresentationId.String, rawBody);

    /// <summary>Wraps a raw body using an ID already durably registered in the owning repository.</summary>
    internal static EncodedBaseObjectBody Encode(RepresentationId representationId, PreparedBaseBody rawBody) {
        if (representationId.Value == 0) {
            throw new ArgumentOutOfRangeException(nameof(representationId), "Representation ID zero is invalid.");
        }
        ArgumentNullException.ThrowIfNull(rawBody);
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteByte(4);
        writer.WriteUInt32(representationId.Value);
        buffer.Write(rawBody.Body);
        // TODO: After MVP, avoid the intermediate buffer/copy when wrapping prepared content.
        return new(buffer.WrittenSpan);
    }

    internal static DecodedBaseObjectBody Decode(ReadOnlySpan<byte> encodedBody, SchemaStore? schemas = null) {
        BinaryPayloadReader reader = new(encodedBody);
        byte version = reader.ReadByte();
        ObjectLayout layout;
        RepresentationId? representationId = null;
        if (version == 4) {
            RepresentationId id = new(reader.ReadUInt32());
            if (id.Value == 0) { throw new InvalidDataException("Representation ID zero is invalid."); }
            if (id == RepresentationId.String && schemas is null) {
                layout = ObjectLayout.String;
            }
            else {
                if (schemas is null) {
                    throw new InvalidDataException($"Representation ID {id.Value} requires its repository SchemaStore.");
                }
                layout = schemas.GetRepresentation(id);
            }
            representationId = id;
        }
        else if (version is >= 1 and <= 3) {
            layout = RepresentationDescriptorCodec.Read(ref reader, ResolveSchema,
                allowArrays: version >= 3, legacySchemaKeys: version == 1);
        }
        else {
            throw new InvalidDataException($"Unsupported Base type header version {version}.");
        }

        return new(layout, representationId, encodedBody[reader.ConsumedCount..]);

        DurableSchema ResolveSchema(SchemaKey key) {
            if (schemas is null || !schemas.TryGet(key, out DurableSchema? schema)) {
                throw new InvalidDataException($"Missing exact Schema '{key.Type}' version {key.Version}.");
            }
            return schema!;
        }
    }
}
