namespace Atelia.DurableGraph.StateStore;

/// <summary>One closed metadata node. Inline nodes are dependencies, never object representations.</summary>
internal sealed class SchemaCatalogEntry {
    private SchemaCatalogEntry(RepresentationId id, DurableSchema? schema, ArrayLayout? array, ListLayout? list = null, DictionaryLayout? dictionary = null) {
        Id = id;
        Schema = schema;
        Array = array;
        List = list;
        Dictionary = dictionary;
        Layout = dictionary is not null ? ObjectLayout.ForDictionary(dictionary)
            : list is not null ? ObjectLayout.ForList(list)
            : array is not null ? ObjectLayout.ForArray(array)
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

    internal static SchemaCatalogEntry ForList(RepresentationId id, ListLayout list) {
        ArgumentNullException.ThrowIfNull(list);
        return new(id, null, null, list);
    }

    internal static SchemaCatalogEntry ForDictionary(RepresentationId id, DictionaryLayout dictionary) {
        ArgumentNullException.ThrowIfNull(dictionary);
        return new(id, null, null, dictionary: dictionary);
    }

    internal RepresentationId Id { get; }
    internal DurableSchema? Schema { get; }
    internal ArrayLayout? Array { get; }
    internal ListLayout? List { get; }
    internal DictionaryLayout? Dictionary { get; }
    internal ObjectLayout? Layout { get; }
}
