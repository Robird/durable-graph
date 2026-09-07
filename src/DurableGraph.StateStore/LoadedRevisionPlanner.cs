using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Consumes only the normalized baseline produced by controlled Load.</summary>
internal static class LoadedRevisionPlanner {
    internal static PreparedObjectRevision Prepare(StateRevisionStore store, SchemaStore schemas,
        NormalizedRevision source, IReadOnlyList<PreparedCapturedObject> contents,
        ReadAmplificationBaseBudgetParameters parameters) {
        IReadOnlyDictionary<uint, FrameAddress> heads = store.ReadLiveObjectHeads(source.RevisionAddress);
        if (heads.Count != source.Objects.Count || source.Objects.Keys.Any(id => !heads.ContainsKey(id))) {
            throw new InvalidDataException("Loaded source membership no longer matches the exact Parent.");
        }
        // Verify complete source provenance before registering any current Schema. This includes
        // source rows no longer reachable from World and every NoChange survivor.
        // TODO(DB-033): Measure these repeated chain reads before sharing a scoped cache with planning.
        foreach ((uint id, NormalizedObject row) in source.Objects) {
            ObjectVersionChain chain = store.ReadObjectVersionChain(source.RevisionAddress, id);
            BaseObjectPayload stored = BaseObjectPayloadCodec.Decode(chain.Records[0].Record.Body);
            if (stored.Kind != row.Current.Kind) {
                throw new InvalidDataException($"Loaded object {id} no longer has its source kind.");
            }
            if (stored.Kind == CapturedObjectKind.String) {
                if (chain.Records.Count != 1 || row.SourceSchema is not null || row.RequiresRewrite) {
                    throw new InvalidDataException("String source provenance is invalid.");
                }
            } else {
                DurableSchema schema = schemas.GetRequired(stored.SchemaKey!.Value);
                if (!schema.Equals(row.SourceSchema) ||
                    row.RequiresRewrite != !schema.Equals(row.Current.Schema) ||
                    row.Model is null || !row.Model.CurrentSchema.Equals(row.Current.Schema)) {
                    throw new InvalidDataException($"Loaded object {id} no longer matches its exact source Schema and normalized model.");
                }
            }
        }
        foreach (PreparedCapturedObject row in contents) {
            bool existed = source.Objects.TryGetValue(row.Current.Id, out NormalizedObject? prior);
            if (existed != (row.Previous is not null) ||
                (existed && (!ReferenceEquals(row.Previous, prior!.Current) ||
                    row.Current.Kind != prior.Current.Kind ||
                    !Equals(row.Current.Schema, prior.Current.Schema)))) {
                throw new InvalidDataException("Prepared contents do not match the controlled normalized baseline.");
            }
        }
        schemas.RegisterBatch(contents.Where(static row => row.Current.Kind == CapturedObjectKind.Durable)
            .Select(static row => row.Current.Schema!));
        List<PreparedObject> rows = [];
        foreach (PreparedCapturedObject row in contents) {
            var body = row.Current.Kind == CapturedObjectKind.String
                ? BaseObjectPayloadCodec.EncodeString(row.BaseContent)
                : BaseObjectPayloadCodec.EncodeDurable(row.Current.Schema!, row.BaseContent);
            if (row.Previous is null) {
                rows.Add(PreparedObject.New(row.Current.Id, body));
            } else if (source.Objects[row.Current.Id].RequiresRewrite) {
                rows.Add(PreparedObject.BaseOnlyUpdate(row.Current.Id, heads[row.Current.Id], body));
            } else {
                rows.Add(row.DeltaContent is null
                    ? PreparedObject.Unchanged(row.Current.Id, heads[row.Current.Id], body)
                    : PreparedObject.Compared(row.Current.Id, heads[row.Current.Id], body, row.DeltaContent));
            }
        }
        // Existing planner derives Removes from complete Parent membership minus these live rows.
        return ObjectRevisionPlanner.PrepareRevision(store, source.RevisionAddress, rows, parameters);
    }
}
