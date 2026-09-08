namespace Atelia.DurableGraph;

/// <summary>Resolves references after the loader has validated DTOs and allocated every reachable domain object.</summary>
/// <remarks>Construction copies the directory. Instances remain unpublished until all hydration succeeds.</remarks>
public sealed class ObjectReadTable {
    private readonly StringReadTable _strings;
    private readonly Dictionary<ObjectId, DurableBase> _objects;

    public ObjectReadTable(StringReadTable strings, IReadOnlyDictionary<ObjectId, DurableBase> objects) {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(objects);
        _strings = strings;
        _objects = [];
        HashSet<DurableBase> instances = new(ReferenceEqualityComparer.Instance);
        foreach ((ObjectId id, DurableBase instance) in objects) {
            if (id.IsNull || instance is null || !instances.Add(instance) || !_objects.TryAdd(id, instance)) {
                throw new InvalidDataException("Allocated durable IDs and instances must be nonzero, non-null and unique.");
            }
        }
    }

    public string? ResolveString(ObjectId id) => _strings.ResolveString(id);

    /// <summary>Resolves null or an already allocated instance compatible with the declared CLR type.</summary>
    public TDomain? ResolveDurable<TDomain>(ObjectId id) where TDomain : DurableBase {
        if (id.IsNull) {
            return null;
        }
        return _objects.TryGetValue(id, out DurableBase? instance) && instance is TDomain typed
            ? typed
            : throw new InvalidDataException($"Object ID {id} is not an allocated {typeof(TDomain)} in this loading view.");
    }
}
