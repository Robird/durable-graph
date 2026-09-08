namespace Atelia.DurableGraph;

/// <summary>Read-only facts for one synchronous, single-object adjacent upgrade invocation.</summary>
/// <remarks>Do not retain this context beyond the invocation. It does not expose graph access or ID allocation.</remarks>
public sealed class UpgradeContext {
    public UpgradeContext(uint objectId, DurableSchema sourceObjectSchema, DurableSchema targetObjectSchema) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
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
    }

    public uint ObjectId { get; }
    public DurableSchema SourceObjectSchema { get; }
    public DurableSchema TargetObjectSchema { get; }
}
