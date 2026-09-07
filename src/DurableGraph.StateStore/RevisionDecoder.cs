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
        Dictionary<SchemaKey, StateReaderBinding> bindings = readers.Snapshot();
        return ReadSnapshot(store, schemas, revisionAddress, bindings);
    }

    internal static DecodedRevision ReadSnapshot(
        StateRevisionStore store,
        SchemaStore schemas,
        FrameAddress revisionAddress,
        IReadOnlyDictionary<SchemaKey, StateReaderBinding> bindings) {
        List<CapturedObject> objects = [];
        List<(CapturedObject Row, StateReaderBinding Binding)> durableRows = [];
        List<(uint Id, string Value)> strings = [];

        // Object-first reconstruction: only one raw chain is retained at a time.
        foreach (uint id in store.ReadLiveObjectHeads(revisionAddress).Keys.Order()) {
            ObjectVersionChain chain = store.ReadObjectVersionChain(revisionAddress, id);
            BaseObjectPayload payload = TypedObjectVersionReader.DecodeBase(chain);
            if (payload.Kind == CapturedObjectKind.String) {
                string value = TypedObjectVersionReader.ReadString(chain, payload);
                objects.Add(new CapturedObject(id, value));
                strings.Add((id, value));
            } else {
                SchemaKey key = payload.SchemaKey
                    ?? throw new InvalidDataException("A durable Base requires an exact Schema key.");
                if (!bindings.TryGetValue(key, out StateReaderBinding? binding)) {
                    throw new InvalidDataException($"No reader is registered for {key.SchemaId} v{key.Version}.");
                }
                TypedObjectVersionReader.MatchSchema(schemas, key, binding.Schema);
                CapturedObject row = binding.Read(id, TypedObjectVersionReader.CreateBodySource(chain, payload));
                objects.Add(row);
                durableRows.Add((row, binding));
            }
        }

        StringReadTable table = StringReadTable.FromDecoded(strings);
        StateReferenceValidator validator = new(objects.ToDictionary(static row => row.Id));
        foreach ((CapturedObject row, StateReaderBinding binding) in durableRows) {
            binding.VisitReferences(row, validator);
        }
        return new DecodedRevision(revisionAddress, objects, table);
    }
}
