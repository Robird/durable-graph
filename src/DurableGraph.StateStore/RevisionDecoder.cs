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
        IReadOnlyDictionary<SchemaKey, StateReaderBinding> bindings) => ReadCore(store, schemas, revisionAddress, schema => {
            SchemaKey key = new(schema.Type, schema.Version);
            if (!bindings.TryGetValue(key, out StateReaderBinding? binding)) {
                throw new InvalidDataException($"No reader is registered for {key.Type} v{key.Version}.");
            }
            return binding;
        });

    internal static DecodedRevision ReadSnapshot(
        StateRevisionStore store, SchemaStore schemas, FrameAddress revisionAddress, StateBindingContext bindings) =>
        ReadCore(store, schemas, revisionAddress, bindings.ResolveReader);

    private static DecodedRevision ReadCore(
        StateRevisionStore store, SchemaStore schemas, FrameAddress revisionAddress,
        Func<DurableSchema, StateReaderBinding> resolveReader) {
        List<ObjectStateRecord> objects = [];
        List<(ObjectStateRecord Row, StateReaderBinding Binding)> durableRows = [];
        List<(ObjectId Id, string Value)> strings = [];

        // Object-first reconstruction: only one raw chain is retained at a time.
        foreach (uint rawId in store.ReadLiveObjectHeadMap(revisionAddress).Keys.Order()) {
            ObjectId id = new(rawId);
            ObjectVersionChain chain = store.ReadObjectVersionChain(revisionAddress, rawId);
            DecodedBaseObjectBody body = TypedObjectVersionReader.DecodeBase(chain);
            if (body.Kind == ObjectStateKind.String) {
                string value = TypedObjectVersionReader.ReadString(chain, body);
                objects.Add(new ObjectStateRecord(id, value));
                strings.Add((id, value));
            } else {
                SchemaKey key = body.SchemaKey
                    ?? throw new InvalidDataException("A durable Base requires an exact Schema key.");
                // The persisted full layout selects the historical execution representation.
                // Current CLR arguments cannot substitute for old inline state versions.
                DurableSchema schema = schemas.GetRequired(key);
                StateReaderBinding binding = resolveReader(schema);
                if (!schema.Equals(binding.Schema)) {
                    throw new InvalidDataException("The selected reader does not match the complete stored Schema.");
                }
                ObjectStateRecord row = binding.Read(id, TypedObjectVersionReader.CreateBodySource(chain, body));
                objects.Add(row);
                durableRows.Add((row, binding));
            }
        }

        StringReadTable table = StringReadTable.FromDecoded(strings);
        StateReferenceValidator validator = new(objects.ToDictionary(static row => row.Id));
        foreach ((ObjectStateRecord row, StateReaderBinding binding) in durableRows) {
            binding.VisitReferences(row, validator);
        }
        return new DecodedRevision(revisionAddress, objects, table);
    }
}
