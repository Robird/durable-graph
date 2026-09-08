using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Consumes only the normalized baseline produced by controlled Load.</summary>
internal static class LoadedRevisionPlanner {
    internal static PreparedObjectRevision Prepare(StateRevisionStore store, SchemaStore schemas,
        NormalizedRevision source, IReadOnlyList<PreparedCapturedObject> contents,
        ReadAmplificationBaseBudgetParameters parameters) {
        IReadOnlyDictionary<ObjectId, FrameAddress> heads = store.ReadLiveObjectHeadMap(source.RevisionAddress).ToDictionary(static pair => new ObjectId(pair.Key), static pair => pair.Value);
        if (heads.Count != source.Objects.Count || source.Objects.Keys.Any(id => !heads.ContainsKey(id))) {
            throw new InvalidDataException("Loaded source membership no longer matches the exact Parent.");
        }
        // Verify complete source provenance before registering any current Schema. This includes
        // source rows no longer reachable from World and every NoChange survivor.
        // TODO(DB-033): Measure these repeated chain reads before sharing a scoped cache with planning.
        foreach ((ObjectId id, NormalizedObject row) in source.Objects) {
            ObjectVersionChain chain = store.ReadObjectVersionChain(source.RevisionAddress, id.Value);
            DecodedBaseObjectBody stored = BaseObjectBodyCodec.Decode(chain.Records[0].Record.Body, schemas);
            if (stored.Kind != row.Current.Kind) {
                throw new InvalidDataException($"Loaded object {id} no longer has its source kind.");
            }
            if (stored.Kind == ObjectStateKind.String) {
                if (chain.Records.Count != 1 || row.SourceSchema is not null || row.RequiresRewrite) {
                    throw new InvalidDataException("String source provenance is invalid.");
                }
            } else {
                ObjectLayout layout = stored.Layout;
                if (!layout.Equals(row.SourceLayout) ||
                    row.RequiresRewrite != !layout.Equals(row.Current.Layout) ||
                    !row.Model.CurrentLayout.Equals(row.Current.Layout)) {
                    throw new InvalidDataException($"Loaded object {id} no longer matches its exact source layout and normalized model.");
                }
            }
        }
        foreach (PreparedCapturedObject row in contents) {
            bool existed = source.Objects.TryGetValue(row.Current.Id, out NormalizedObject? prior);
            if (existed != (row.Previous is not null) ||
                (existed && (!ReferenceEquals(row.Previous, prior!.Current) ||
                    row.Current.Kind != prior.Current.Kind ||
                    !row.Current.Layout.Equals(prior.Current.Layout)))) {
                throw new InvalidDataException("Prepared contents do not match the controlled normalized baseline.");
            }
        }
        RepresentationId[] representations = schemas.RegisterRepresentations(
            contents.Select(static row => row.Current.Layout).ToArray());
        List<PreparedObject> rows = [];
        for (int index = 0; index < contents.Count; index++) {
            PreparedCapturedObject row = contents[index];
            EncodedBaseObjectBody encodedBaseBody = BaseObjectBodyCodec.Encode(representations[index], row.BaseBody);
            if (row.Previous is null) {
                rows.Add(PreparedObject.New(row.Current.Id, encodedBaseBody));
            } else if (source.Objects[row.Current.Id].RequiresRewrite) {
                rows.Add(PreparedObject.BaseOnlyUpdate(row.Current.Id, heads[row.Current.Id], encodedBaseBody));
            } else {
                rows.Add(row.DeltaBody is null
                    ? PreparedObject.Unchanged(row.Current.Id, heads[row.Current.Id], encodedBaseBody)
                    : PreparedObject.Compared(row.Current.Id, heads[row.Current.Id], encodedBaseBody, row.DeltaBody));
            }
        }
        // Existing planner derives Removes from complete Parent membership minus these live rows.
        return ObjectRevisionPlanner.PrepareRevision(store, source.RevisionAddress, rows, parameters);
    }
}
