namespace Atelia.DurableGraph;

/// <summary>A prebound synchronous value conversion in the current owner invocation.</summary>
public delegate TNext ValueUpgrade<TPrior, TNext>(in TPrior prior) where TPrior : unmanaged where TNext : unmanaged;

/// <summary>Read-only facts for one synchronous, single-object adjacent upgrade invocation.</summary>
/// <remarks>Do not retain this context beyond the invocation. It does not expose graph access or ID allocation.</remarks>
public sealed class UpgradeContext {
    private readonly IReadOnlyDictionary<string, Delegate> _tools;

    public UpgradeContext(ObjectId objectId, DurableSchema sourceObjectSchema, DurableSchema targetObjectSchema) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId.Value, nameof(objectId));
        ArgumentNullException.ThrowIfNull(sourceObjectSchema);
        ArgumentNullException.ThrowIfNull(targetObjectSchema);
        sourceObjectSchema.RequireReferenceObject();
        targetObjectSchema.RequireReferenceObject();
        if (sourceObjectSchema.Type != targetObjectSchema.Type ||
            sourceObjectSchema.Version == int.MaxValue || targetObjectSchema.Version != sourceObjectSchema.Version + 1) {
            throw new ArgumentException("Upgrade context endpoints must be adjacent versions of the same closed object family.");
        }
        ObjectId = objectId;
        SourceObjectSchema = sourceObjectSchema;
        TargetObjectSchema = targetObjectSchema;
        _tools = new Dictionary<string, Delegate>(StringComparer.Ordinal);
    }

    internal UpgradeContext(ObjectId objectId, DurableSchema sourceObjectSchema, DurableSchema targetObjectSchema,
        IReadOnlyDictionary<string, Delegate> tools) : this(objectId, sourceObjectSchema, targetObjectSchema) => _tools = tools;

    public ObjectId ObjectId { get; }
    public DurableSchema SourceObjectSchema { get; }
    public DurableSchema TargetObjectSchema { get; }

    /// <summary>Gets an already declared tool from this provider's local scope; it never selects or binds new rules.</summary>
    public ValueUpgrade<TPrior, TNext> GetValueUpgrade<TPrior, TNext>(string key)
        where TPrior : unmanaged where TNext : unmanaged {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_tools.TryGetValue(key, out Delegate? value)) { throw new InvalidOperationException($"No value upgrade tool named '{key}' is declared in this provider scope."); }
        return value as ValueUpgrade<TPrior, TNext> ??
            throw new InvalidOperationException($"Value upgrade tool '{key}' has different state endpoint types.");
    }
}
