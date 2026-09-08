namespace Atelia.DurableGraph;

/// <summary>Receives only reference slots from a frozen DTO, independently of its byte body.</summary>
public interface IStateReferenceVisitor {
    void VisitString(uint id);
    void VisitDurable(uint id, string nominalSchemaId);

    /// <summary>Visits a reference constrained by a complete constructed nominal identity.</summary>
    void VisitDurable(uint id, TypeExpr nominalType) {
        ArgumentNullException.ThrowIfNull(nominalType);
        if (nominalType.Kind != TypeExprKind.Named || !nominalType.IsClosed) {
            throw new ArgumentException("A reference requires a closed named type.", nameof(nominalType));
        }
        if (!nominalType.Arguments.IsEmpty) {
            throw new NotSupportedException("This visitor does not support constructed nominal identities.");
        }
        VisitDurable(id, nominalType.DefinitionId!);
    }
}

/// <summary>Visits all inherited and declared reference slots in one exact-version DTO.</summary>
public delegate void StateReferenceVisitor<TState>(in TState state, IStateReferenceVisitor visitor)
    where TState : unmanaged;

/// <summary>Validates references against one complete stored or current DTO directory.</summary>
/// <remarks>Keep the directory stable while visiting. Schema ancestry comes from this view, never current CLR ancestry.</remarks>
public sealed class StateReferenceValidator : IStateReferenceVisitor {
    private readonly IReadOnlyDictionary<uint, ObjectStateRecord> _objects;

    public StateReferenceValidator(IReadOnlyDictionary<uint, ObjectStateRecord> objects) {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects;
    }

    public void VisitString(uint id) {
        if (id != 0 && (!_objects.TryGetValue(id, out ObjectStateRecord? item) || item.Kind != ObjectStateKind.String)) {
            throw new InvalidDataException($"Object ID {id} is not a string in this DTO view.");
        }
    }

    public void VisitDurable(uint id, string nominalSchemaId) => VisitDurable(id, TypeExpr.Named(nominalSchemaId));

    public void VisitDurable(uint id, TypeExpr nominalType) {
        RequireNominal(nominalType);
        if (id == 0) {
            return;
        }
        if (!_objects.TryGetValue(id, out ObjectStateRecord? item) || item.Kind != ObjectStateKind.Durable ||
            !Accepts(item.Schema!, nominalType)) {
            throw new InvalidDataException($"Object ID {id} does not satisfy nominal Schema {nominalType} in this DTO view.");
        }
    }

    internal static bool Accepts(DurableSchema schema, string nominalSchemaId) => Accepts(schema, TypeExpr.Named(nominalSchemaId));

    internal static bool Accepts(DurableSchema schema, TypeExpr nominalType) {
        RequireNominal(nominalType);
        if (schema.Kind != SchemaKind.ReferenceObject) { return false; }
        for (DurableSchema? current = schema; current is not null; current = current.BaseSchema) {
            if (current.Type.Equals(nominalType)) {
                return true;
            }
        }
        return false;
    }

    private static void RequireNominal(TypeExpr nominalType) {
        ArgumentNullException.ThrowIfNull(nominalType);
        if (nominalType.Kind != TypeExprKind.Named || !nominalType.IsClosed) {
            throw new ArgumentException("A reference requires a closed named type.", nameof(nominalType));
        }
    }
}
