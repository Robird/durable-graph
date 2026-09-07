using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One current DTO baseline, with complete source membership and migration provenance.</summary>
internal sealed class NormalizedRevision {
    private NormalizedRevision(FrameAddress address, Dictionary<uint, NormalizedObject> objects, StringReadTable strings) {
        RevisionAddress = address;
        Objects = objects;
        Strings = strings;
    }

    internal FrameAddress RevisionAddress { get; }
    internal IReadOnlyDictionary<uint, NormalizedObject> Objects { get; }
    internal StringReadTable Strings { get; }

    internal static NormalizedRevision Create(DecodedRevision source, StateModelSnapshot models) {
        Dictionary<uint, NormalizedObject> normalized = [];
        foreach (CapturedObject row in source.Objects) {
            if (row.Kind == CapturedObjectKind.String) {
                normalized.Add(row.Id, new(row, null, false, null));
                continue;
            }
            DurableSchema storedSchema = row.Schema!;
            if (!models.Models.TryGetValue(storedSchema.SchemaId, out StateModelBinding? model)) {
                throw new InvalidDataException($"No current model is registered for {storedSchema.SchemaId}.");
            }
            CapturedObject current = model.Normalize(row);
            if (current.Id != row.Id || current.Kind != CapturedObjectKind.Durable ||
                !model.CurrentSchema.Equals(current.Schema)) {
                throw new InvalidDataException("Normalization must preserve object identity and produce the exact current Schema.");
            }
            normalized.Add(row.Id, new(current, storedSchema, !storedSchema.Equals(current.Schema), model));
        }
        // The complete old chains were decoded first. Validate current references only after
        // every single-object Upgrade completed; source rows remain live even after an edge is cut.
        StateReferenceValidator validator = new(normalized.ToDictionary(static pair => pair.Key, static pair => pair.Value.Current));
        foreach (NormalizedObject row in normalized.Values) {
            row.Model?.VisitReferences(row.Current, validator);
        }
        return new(source.RevisionAddress, normalized, source.Strings);
    }
}

internal sealed record NormalizedObject(
    CapturedObject Current,
    DurableSchema? SourceSchema,
    bool RequiresRewrite,
    StateModelBinding? Model);
