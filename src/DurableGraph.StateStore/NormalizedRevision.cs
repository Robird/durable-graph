using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One current DTO baseline, with complete source membership and migration provenance.</summary>
internal sealed class NormalizedRevision {
    private readonly Dictionary<ObjectId, NormalizedObject> _objects;

    private NormalizedRevision(FrameAddress address, Dictionary<ObjectId, NormalizedObject> objects, StringReadTable strings, IReadOnlyDictionary<ObjectId, ObjectStateRecord>? currentDtos = null) {
        RevisionAddress = address;
        _objects = objects;
        Strings = strings;
        CurrentDtos = currentDtos ?? objects.ToDictionary(static pair => pair.Key, static pair => pair.Value.Current);
    }

    internal FrameAddress RevisionAddress { get; }
    internal IReadOnlyDictionary<ObjectId, NormalizedObject> Objects => _objects;
    internal StringReadTable Strings { get; }
    internal IReadOnlyDictionary<ObjectId, ObjectStateRecord> CurrentDtos { get; }

    // Candidate rows and provenance are prepared before any State append. Only the outer
    // address wrapper is completed once Append returns, still before publication.
    internal static NormalizedRevision FromCandidate(CapturedGraph candidate, StateModelSnapshot models) {
        Dictionary<ObjectId, NormalizedObject> rows = [];
        foreach (ObjectStateRecord row in candidate.Objects) {
            StateModelBinding? model = row.Kind == ObjectStateKind.Durable
                ? models.ResolveCurrentModel(row.Schema!.Type) : null;
            rows.Add(row.Id, new(row, row.Schema, false, model));
        }
        StringReadTable strings = StringReadTable.FromDecoded(candidate.Objects
            .Where(static row => row.Kind == ObjectStateKind.String)
            .Select(static row => (row.Id, row.StringContent)));
        return new(default, rows, strings);
    }

    internal NormalizedRevision WithAddress(FrameAddress address) =>
        new(address, _objects, Strings, CurrentDtos);

    internal static NormalizedRevision Create(DecodedRevision source, StateModelSnapshot models) {
        Dictionary<ObjectId, NormalizedObject> normalized = [];
        foreach (ObjectStateRecord row in source.Objects) {
            if (row.Kind == ObjectStateKind.String) {
                normalized.Add(row.Id, new(row, null, false, null));
                continue;
            }
            DurableSchema storedSchema = row.Schema!;
            StateModelBinding model = models.ResolveCurrentModel(storedSchema.Type);
            ObjectStateRecord current = model.Normalize(row);
            if (current.Id != row.Id || current.Kind != ObjectStateKind.Durable ||
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
    ObjectStateRecord Current,
    DurableSchema? SourceSchema,
    bool RequiresRewrite,
    StateModelBinding? Model);
