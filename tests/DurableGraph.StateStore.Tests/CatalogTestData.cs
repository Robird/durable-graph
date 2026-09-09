using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Tests;

/// <summary>Unchecked fixture encoding for corrupt catalog logs; never use the validating product writer to manufacture a negative case.</summary>
internal static class CatalogTestData {
    internal static readonly IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry> Empty =
        new Dictionary<RepresentationId, SchemaCatalogEntry>();

    internal static SchemaCatalogEntry[] Schemas(IEnumerable<DurableSchema> schemas, uint firstId = 2) =>
        schemas.Select(schema => SchemaCatalogEntry.ForSchema(new(firstId++), schema)).ToArray();

    internal static Dictionary<RepresentationId, SchemaCatalogEntry> Index(IEnumerable<SchemaCatalogEntry> entries) =>
        entries.ToDictionary(static entry => entry.Id);

    internal static byte[] Encode(IReadOnlyList<SchemaCatalogEntry> entries,
        IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry>? registered = null) {
        Dictionary<SchemaKey, RepresentationId> ids = new();
        foreach (SchemaCatalogEntry entry in (registered ?? Empty).Values.Concat(entries)) {
            if (entry.Schema is { } schema) { ids[new(schema.Type, schema.Version)] = entry.Id; }
        }
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        writer.WriteByte(1);
        writer.WriteUInt32((uint)entries.Count);
        foreach (SchemaCatalogEntry entry in entries) {
            writer.WriteUInt32(entry.Id.Value);
            if (entry.Schema is { } schema) {
                writer.WriteByte((byte)schema.Kind);
                TypeExprWireCodec.Write(ref writer, schema.Type);
                writer.WriteUInt32((uint)schema.Version);
                writer.WriteUInt32(schema.BaseSchema is { } ancestor ? ids[new(ancestor.Type, ancestor.Version)].Value : 0);
                writer.WriteUInt32((uint)schema.Fields.Length);
                foreach (DurableFieldInfo field in schema.Fields) {
                    writer.WriteUInt32((uint)field.FieldId);
                    WriteSlot(ref writer, field, ids);
                }
            }
            else {
                writer.WriteByte(3);
                writer.WriteUInt32(entry.Array!.CodecVersion);
                writer.WriteByte((byte)entry.Array.Constructor);
                WriteSlot(ref writer, entry.Array.ElementSlot, ids);
            }
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSlot(ref BinaryPayloadWriter writer, DurableFieldInfo slot, Dictionary<SchemaKey, RepresentationId> ids) {
        writer.WriteByte((byte)slot.TypeTag);
        if (slot.TypeTag == TypeTag.ObjectReference) { TypeExprWireCodec.Write(ref writer, slot.TargetType!); }
        else if (slot.TypeTag == TypeTag.InlineValue) {
            writer.WriteUInt32(ids[new(slot.InlineSchema!.Type, slot.InlineSchema.Version)].Value);
        }
    }
}
