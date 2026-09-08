using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Encodes the versioned type header carried by Base records only.</summary>
/// <remarks>
/// Version 3 adds tag 3 for an exact built-in array layout. Tag 1 is string;
/// tag 2 carries a closed named type expression and Schema version.
/// Versions 1 and 2 retain their original grammar (no array type constructors).
/// Remaining bytes are the raw body; this codec neither validates that body nor registers Schema.
/// Delta bodies have no header and inherit the Base's exact interpretation.
/// </remarks>
internal static class BaseObjectBodyCodec {
    internal static EncodedBaseObjectBody EncodeString(PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(rawBody);
        return Encode(ObjectLayout.String, rawBody);
    }

    /// <summary>Wraps a raw body. The caller must register its Schema before saving the object.</summary>
    internal static EncodedBaseObjectBody EncodeDurable(DurableSchema schema, PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Kind != SchemaKind.ReferenceObject) {
            throw new ArgumentException("Only reference-object Schemas may identify an object Base.", nameof(schema));
        }
        ArgumentNullException.ThrowIfNull(rawBody);
        return Encode(ObjectLayout.ForDurable(schema), rawBody);
    }

    internal static EncodedBaseObjectBody EncodeArray(ArrayLayout layout, PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(layout);
        return Encode(ObjectLayout.ForArray(layout), rawBody);
    }

    internal static DecodedBaseObjectBody Decode(ReadOnlySpan<byte> encodedBody, SchemaStore? schemas = null) {
        BinaryPayloadReader reader = new(encodedBody);
        byte version = reader.ReadByte();
        if (version is < 1 or > 3) {
            throw new InvalidDataException($"Unsupported Base type header version {version}.");
        }

        byte tag = reader.ReadByte();
        ObjectStateKind kind;
        SchemaKey? key;
        ArrayLayout? arrayLayout = null;
        switch (tag) {
            case 1:
                kind = ObjectStateKind.String;
                key = null;
                break;
            case 2:
                kind = ObjectStateKind.Durable;
                key = version == 1 ? SchemaKeyWireCodec.ReadLegacy(ref reader) : SchemaKeyWireCodec.Read(ref reader, allowArrays: version >= 3);
                break;
            case 3 when version >= 3:
                kind = ObjectStateKind.Array;
                key = null;
                arrayLayout = ReadArrayLayout(ref reader, schemas);
                break;
            default:
                throw new InvalidDataException($"Unsupported Base type tag {tag}.");
        }

        return new(kind, key, encodedBody[reader.ConsumedCount..], arrayLayout);
    }

    internal static EncodedBaseObjectBody Encode(ObjectLayout layout, PreparedBaseBody rawBody) {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(rawBody);
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteByte(3);
        switch (layout.Kind) {
            case ObjectStateKind.String:
                writer.WriteByte(1);
                break;
            case ObjectStateKind.Durable:
                writer.WriteByte(2);
                SchemaKeyWireCodec.Write(ref writer, new(layout.Schema!.Type, layout.Schema.Version));
                break;
            case ObjectStateKind.Array:
                writer.WriteByte(3);
                ArrayLayout array = layout.Array!;
                writer.WriteUInt32(array.CodecVersion);
                writer.WriteByte((byte)array.Constructor);
                DurableFieldInfo element = array.ElementSlot;
                writer.WriteByte((byte)element.TypeTag);
                if (element.TypeTag == TypeTag.ObjectReference) {
                    TypeExprWireCodec.Write(ref writer, element.TargetType!);
                }
                else if (element.TypeTag == TypeTag.InlineValue) {
                    DurableSchema inline = element.InlineSchema!;
                    SchemaKeyWireCodec.Write(ref writer, new(inline.Type, inline.Version));
                }
                break;
            default:
                throw new ArgumentException("Unsupported object layout.", nameof(layout));
        }
        buffer.Write(rawBody.Body);
        // TODO: After MVP, avoid the intermediate buffer/copy when wrapping prepared content.
        return new(buffer.WrittenSpan);
    }

    private static ArrayLayout ReadArrayLayout(ref BinaryPayloadReader reader, SchemaStore? schemas) {
        uint codecVersion = reader.ReadUInt32();
        if (codecVersion != 1) { throw new InvalidDataException($"Unsupported array codec version {codecVersion}."); }
        byte constructor = reader.ReadByte();
        if (constructor is < 4 or > 7) { throw new InvalidDataException("Unknown array constructor."); }
        byte tag = reader.ReadByte();
        DurableFieldInfo element;
        if (tag is >= 1 and <= 14) {
            element = new(1, (TypeTag)tag);
        }
        else if (tag == 15) {
            TypeExpr target = TypeExprWireCodec.Read(ref reader);
            if (target.Kind != TypeExprKind.Named && !target.IsArray) {
                throw new InvalidDataException("An object reference element requires a closed named or array type.");
            }
            element = DurableFieldInfo.Reference(1, target);
        }
        else if (tag == 16) {
            SchemaKey key = SchemaKeyWireCodec.Read(ref reader);
            if (schemas is null || !schemas.TryGet(key, out DurableSchema? schema)) {
                throw new InvalidDataException($"Missing exact array element Schema '{key.Type}' version {key.Version}.");
            }
            if (schema!.Kind != SchemaKind.InlineValue) {
                throw new InvalidDataException("An inline array element requires an inline-value Schema.");
            }
            element = new(1, TypeTag.InlineValue, inlineSchema: schema);
        }
        else { throw new InvalidDataException($"Unknown array element slot tag {tag}."); }
        try { return new((TypeExprKind)constructor, element, codecVersion); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid exact array layout.", error); }
    }
}
