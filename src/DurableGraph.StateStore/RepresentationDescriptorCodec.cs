using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

/// <summary>The SchemaStore-owned descriptor grammar; also reads the old inline Base descriptors.</summary>
internal static class RepresentationDescriptorCodec {
    internal static void Write(ref BinaryPayloadWriter writer, ObjectLayout layout) {
        ArgumentNullException.ThrowIfNull(layout);
        switch (layout.Kind) {
            case ObjectStateKind.String:
                writer.WriteByte(1);
                return;
            case ObjectStateKind.Durable:
                writer.WriteByte(2);
                SchemaKeyWireCodec.Write(ref writer, SchemaBatchWireCodec.Key(layout.Schema!));
                return;
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
                    SchemaKeyWireCodec.Write(ref writer, SchemaBatchWireCodec.Key(element.InlineSchema!));
                }
                return;
            default:
                throw new ArgumentException("Unsupported object representation.", nameof(layout));
        }
    }

    internal static ObjectLayout Read(ref BinaryPayloadReader reader,
        Func<SchemaKey, DurableSchema> resolveSchema, bool allowArrays = true, bool legacySchemaKeys = false) {
        ArgumentNullException.ThrowIfNull(resolveSchema);
        byte kind = reader.ReadByte();
        if (kind == 1) { return ObjectLayout.String; }
        if (kind == 2) {
            SchemaKey key = legacySchemaKeys ? SchemaKeyWireCodec.ReadLegacy(ref reader)
                : SchemaKeyWireCodec.Read(ref reader, allowArrays);
            DurableSchema schema = ResolveExact(key, resolveSchema, SchemaKind.ReferenceObject);
            return ObjectLayout.ForDurable(schema);
        }
        if (kind != 3 || !allowArrays || legacySchemaKeys) {
            throw new InvalidDataException($"Unsupported object representation tag {kind}.");
        }
        uint codecVersion = reader.ReadUInt32();
        if (codecVersion != 1) { throw new InvalidDataException($"Unsupported array codec version {codecVersion}."); }
        byte constructor = reader.ReadByte();
        if (constructor is < 4 or > 7) { throw new InvalidDataException("Unknown array constructor."); }
        byte tag = reader.ReadByte();
        DurableFieldInfo element;
        if (tag is >= 1 and <= 14) { element = new(1, (TypeTag)tag); }
        else if (tag == 15) {
            TypeExpr target = TypeExprWireCodec.Read(ref reader);
            if (target.Kind != TypeExprKind.Named && !target.IsArray) {
                throw new InvalidDataException("An object reference element requires a closed named or array type.");
            }
            element = DurableFieldInfo.Reference(1, target);
        }
        else if (tag == 16) {
            SchemaKey key = SchemaKeyWireCodec.Read(ref reader);
            element = new(1, TypeTag.InlineValue, inlineSchema: ResolveExact(key, resolveSchema, SchemaKind.InlineValue));
        }
        else { throw new InvalidDataException($"Unknown array element slot tag {tag}."); }
        try { return ObjectLayout.ForArray(new((TypeExprKind)constructor, element, codecVersion)); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid exact array layout.", error); }
    }

    private static DurableSchema ResolveExact(SchemaKey key, Func<SchemaKey, DurableSchema> resolver, SchemaKind kind) {
        DurableSchema schema = resolver(key);
        if (schema is null || schema.Kind != kind || SchemaBatchWireCodec.Key(schema) != key) {
            throw new InvalidDataException($"Schema '{key.Type}' version {key.Version} has the wrong representation identity or kind.");
        }
        return schema;
    }
}
