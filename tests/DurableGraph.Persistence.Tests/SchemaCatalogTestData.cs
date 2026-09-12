using Atelia.DurableGraph.Schema;
namespace Atelia.DurableGraph.Persistence.Tests;

internal static class SchemaCatalogTestData {
    internal static readonly IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry> Empty =
        new Dictionary<RepresentationId, SchemaCatalogEntry>();

    // Inputs explicitly use dependency order; this helper does not discover or sort the closure.
    internal static SchemaCatalogEntry[] Entries(params DurableSchema[] schemas) => schemas
        .Select((schema, index) => SchemaCatalogEntry.ForSchema(new((uint)index + 2), schema)).ToArray();

    internal static Dictionary<RepresentationId, SchemaCatalogEntry> Registered(params DurableSchema[] schemas) =>
        Entries(schemas).ToDictionary(entry => entry.Id);

    internal static byte[] Write(DurableSchema[] schemas,
        IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry>? registered = null) {
        registered ??= Empty;
        uint first = registered.Count == 0 ? 2 : checked(registered.Keys.Max(id => id.Value) + 1);
        return SchemaCatalogWireCodec.Write(schemas.Select((schema, index) =>
            SchemaCatalogEntry.ForSchema(new(first + (uint)index), schema)).ToArray(), registered);
    }

    internal static Dictionary<SchemaKey, DurableSchema> Read(byte[] bytes,
        IReadOnlyDictionary<RepresentationId, SchemaCatalogEntry>? registered = null) =>
        SchemaCatalogWireCodec.Read(bytes, registered ?? Empty).Where(entry => entry.Schema is not null)
            .ToDictionary(entry => new SchemaKey(entry.Schema!.Type, entry.Schema!.Version), entry => entry.Schema!);
}
