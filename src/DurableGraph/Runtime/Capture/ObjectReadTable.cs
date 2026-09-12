namespace Atelia.DurableGraph.Runtime;

/// <summary>Resolves references after the loader has validated DTOs and allocated every reachable domain object.</summary>
/// <remarks>Construction copies the directory. Instances remain unpublished until all hydration succeeds.</remarks>
public sealed class ObjectReadTable {
    private readonly Dictionary<ObjectId, object> _objects;

    public ObjectReadTable(StringReadTable strings, IReadOnlyDictionary<ObjectId, IDurableObject> objects)
        : this(Merge(strings, objects)) { }

    /// <summary>Copies one unified directory. Only empty strings may share an instance across distinct IDs.</summary>
    public ObjectReadTable(IReadOnlyDictionary<ObjectId, object> objects) {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = [];
        HashSet<object> instances = new(ReferenceEqualityComparer.Instance);
        foreach ((ObjectId id, object instance) in objects) {
            if (id.IsNull || instance is null || instance.GetType().IsValueType) {
                throw new InvalidDataException("Allocated object IDs and reference instances must be nonzero and non-null.");
            }
            object canonical = instance is string { Length: 0 } ? string.Empty : instance;
            if ((canonical is not string { Length: 0 } && !instances.Add(canonical)) || !_objects.TryAdd(id, canonical)) {
                throw new InvalidDataException("Different object IDs require distinct instances, except empty strings.");
            }
        }
    }

    public string? ResolveString(ObjectId id) => ResolveObject<string>(id);

    /// <summary>Resolves null or an already allocated instance compatible with the declared CLR type.</summary>
    public TDomain? ResolveDurable<TDomain>(ObjectId id) where TDomain : class, IDurableObject => ResolveObject<TDomain>(id);

    /// <summary>Resolves a reference from the common allocated-object directory.</summary>
    public T? ResolveObject<T>(ObjectId id) where T : class {
        if (id.IsNull) {
            return null;
        }
        return _objects.TryGetValue(id, out object? instance) && instance is T typed &&
            (!(typeof(T).IsArray || (typeof(T).IsGenericType &&
                (typeof(T).GetGenericTypeDefinition() == typeof(List<>) || typeof(T).GetGenericTypeDefinition() == typeof(Dictionary<,>)))) ||
                instance.GetType() == typeof(T))
            ? typed
            : throw new InvalidDataException($"Object ID {id} is not an allocated {typeof(T)} in this loading view.");
    }

    private static Dictionary<ObjectId, object> Merge(StringReadTable strings, IReadOnlyDictionary<ObjectId, IDurableObject> objects) {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(objects);
        Dictionary<ObjectId, object> merged = [];
        foreach ((ObjectId id, string value) in strings.Entries) { merged.Add(id, value); }
        foreach ((ObjectId id, IDurableObject value) in objects) {
            if (!merged.TryAdd(id, value)) { throw new InvalidDataException("String and durable directories contain the same object ID."); }
        }
        return merged;
    }
}
