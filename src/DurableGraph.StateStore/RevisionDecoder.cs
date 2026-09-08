using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Reads all live DTO/string content using Base type information and explicit model readers.</summary>
public static class RevisionDecoder {
    /// <summary>
    /// Reconstructs stored-exact versions and validates all supported references before returning.
    /// Failure returns no partial directory and does not write either store. Callers supply stores
    /// belonging to the same repository and keep them stable during this synchronous operation.
    /// </summary>
    public static DecodedRevision Read(
        StateRevisionStore store,
        SchemaStore schemas,
        FrameAddress revisionAddress,
        StateReaderRegistry readers) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(readers);
        StateModelSnapshot bindings = readers.Snapshot(schemas);
        return ReadSnapshot(store, schemas, revisionAddress, bindings);
    }

    internal static DecodedRevision ReadSnapshot(
        StateRevisionStore store,
        SchemaStore schemas,
        FrameAddress revisionAddress,
        IReadOnlyDictionary<SchemaKey, StateReaderBinding> bindings) => ReadCore(store, schemas, revisionAddress, body => {
            ObjectLayout layout = body.Layout;
            if (layout.Kind == ObjectStateKind.String) { return StringObjectReader.Instance; }
            DurableSchema schema = layout.Schema ?? throw new InvalidDataException("Array readers require a model catalog.");
            SchemaKey key = new(schema.Type, schema.Version);
            if (!bindings.TryGetValue(key, out StateReaderBinding? binding)) {
                throw new InvalidDataException($"No reader is registered for {key.Type} v{key.Version}.");
            }
            return binding;
        });

    internal static DecodedRevision ReadSnapshot(
        StateRevisionStore store, SchemaStore schemas, FrameAddress revisionAddress, StateBindingContext bindings) =>
        ReadCore(store, schemas, revisionAddress, body => body.RepresentationId is { } id
            ? schemas.ResolveReader(id, bindings)
            : bindings.ResolveObjectReader(body.Layout));

    private static DecodedRevision ReadCore(
        StateRevisionStore store, SchemaStore schemas, FrameAddress revisionAddress,
        Func<DecodedBaseObjectBody, ObjectReaderBinding> resolveReader) {
        List<ObjectStateRecord> objects = [];
        List<(ObjectStateRecord Row, ObjectReaderBinding Binding)> boundRows = [];
        List<(ObjectId Id, string Value)> strings = [];

        // Object-first reconstruction: only one raw chain is retained at a time.
        foreach (uint rawId in store.ReadLiveObjectHeadMap(revisionAddress).Keys.Order()) {
            ObjectId id = new(rawId);
            ObjectVersionChain chain = store.ReadObjectVersionChain(revisionAddress, rawId);
            DecodedBaseObjectBody body = TypedObjectVersionReader.DecodeBase(chain, schemas);
            ObjectLayout layout = body.Layout;
            ObjectReaderBinding binding = resolveReader(body);
            if (!layout.Equals(binding.Layout)) {
                throw new InvalidDataException("The selected reader does not match the complete stored layout.");
            }
            ObjectStateRecord row = binding.Read(id, TypedObjectVersionReader.CreateBodySource(chain, body));
            objects.Add(row);
            boundRows.Add((row, binding));
            if (row.Kind == ObjectStateKind.String) { strings.Add((id, row.StringContent)); }
        }

        StringReadTable table = StringReadTable.FromDecoded(strings);
        StateReferenceValidator validator = new(objects.ToDictionary(static row => row.Id));
        foreach ((ObjectStateRecord row, ObjectReaderBinding binding) in boundRows) {
            binding.VisitReferences(row, validator);
        }
        return new DecodedRevision(revisionAddress, objects, table);
    }
}
