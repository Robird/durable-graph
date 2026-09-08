using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Maps object representation metadata at the typed persistence boundary.</summary>
internal static class ObjectPersistence {
    internal static ObjectLayout GetLayout(DecodedBaseObjectBody body, SchemaStore schemas) => body.Kind switch {
        ObjectStateKind.String => ObjectLayout.String,
        ObjectStateKind.Durable => ObjectLayout.ForDurable(schemas.GetRequired(body.SchemaKey!.Value)),
        ObjectStateKind.Array => ObjectLayout.ForArray(body.ArrayLayout!),
        _ => throw new InvalidDataException("Unknown persisted object kind."),
    };

    internal static EncodedBaseObjectBody Encode(ObjectStateRecord record, PreparedBaseBody body) =>
        BaseObjectBodyCodec.Encode(record.Layout, body);

    internal static void RegisterSchemas(SchemaStore schemas, IEnumerable<ObjectStateRecord> records) =>
        schemas.RegisterBatch(records.SelectMany(static record => {
            DurableSchema? schema = record.Layout.Schema ?? record.Layout.Array?.ElementSlot.InlineSchema;
            return schema is null ? Array.Empty<DurableSchema>() : new[] { schema };
        }));
}
