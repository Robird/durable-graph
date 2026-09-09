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
            ObjectBinding model = ResolveModel(models, row.Layout);
            rows.Add(row.Id, new(row, row.Layout, false, model));
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
            ObjectBinding model = ResolveModel(models, row.Layout);
            ObjectStateRecord current = model.Normalize(row);
            if (current.Id != row.Id || current.Kind != row.Kind ||
                !model.CurrentLayout.Equals(current.Layout)) {
                throw new InvalidDataException("Normalization must preserve object identity and produce the exact current layout.");
            }
            normalized.Add(row.Id, new(current, row.Layout, !row.Layout.Equals(current.Layout), model));
        }
        // The complete old chains were decoded first. Validate current references only after
        // every single-object Upgrade completed; source rows remain live even after an edge is cut.
        Dictionary<ObjectId, ObjectStateRecord> directory = normalized.ToDictionary(static pair => pair.Key, static pair => pair.Value.Current);
        StateReferenceValidator validator = new(directory);
        foreach (NormalizedObject row in normalized.Values) {
            row.Model?.VisitReferences(row.Current, validator);
        }
        // Validate all source members even if an Upgrade removed their last incoming edge.
        foreach (NormalizedObject row in normalized.Values) {
            if (row.Model is DictionaryObjectBinding dictionary) {
                dictionary.ValidateLookupKeys(row.Current, id => directory.TryGetValue(id, out ObjectStateRecord? target)
                    ? target : throw new InvalidDataException($"Dictionary key object {id.Value} is not live."));
            }
        }
        return new(source.RevisionAddress, normalized, source.Strings, directory);
    }

    private static ObjectBinding ResolveModel(StateModelSnapshot models, ObjectLayout layout) =>
        models.TryGetCurrentObjectBinding(models.GetDomainType(layout.Type), out ObjectBinding? binding) ? binding! :
        throw new InvalidDataException($"No current object binding exists for {layout.Type}.");
}

internal sealed record NormalizedObject(
    ObjectStateRecord Current,
    ObjectLayout SourceLayout,
    bool RequiresRewrite,
    ObjectBinding Model) {
    internal DurableSchema? SourceSchema => SourceLayout.Schema;
}
