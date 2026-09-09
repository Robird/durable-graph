namespace Atelia.DurableGraph.StateStore;

/// <summary>One closed metadata node. Inline nodes are dependencies, never object representations.</summary>
internal sealed class SchemaCatalogEntry {
    private SchemaCatalogEntry(RepresentationId id, DurableSchema? schema, ArrayLayout? array) {
        Id = id;
        Schema = schema;
        Array = array;
        Layout = array is not null ? ObjectLayout.ForArray(array)
            : schema!.Kind == SchemaKind.ReferenceObject ? ObjectLayout.ForDurable(schema) : null;
    }

    internal static SchemaCatalogEntry ForSchema(RepresentationId id, DurableSchema schema) {
        ArgumentNullException.ThrowIfNull(schema);
        return new(id, schema, null);
    }

    internal static SchemaCatalogEntry ForArray(RepresentationId id, ArrayLayout array) {
        ArgumentNullException.ThrowIfNull(array);
        return new(id, null, array);
    }

    internal RepresentationId Id { get; }
    internal DurableSchema? Schema { get; }
    internal ArrayLayout? Array { get; }
    internal ObjectLayout? Layout { get; }
}
