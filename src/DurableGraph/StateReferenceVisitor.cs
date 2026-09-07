namespace Atelia.DurableGraph;

/// <summary>Receives only reference slots from a frozen DTO, independently of its byte body.</summary>
public interface IStateReferenceVisitor {
    void VisitString(uint id);
    void VisitDurable(uint id, string nominalSchemaId);
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

    public void VisitDurable(uint id, string nominalSchemaId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(nominalSchemaId);
        if (id == 0) {
            return;
        }
        if (!_objects.TryGetValue(id, out ObjectStateRecord? item) || item.Kind != ObjectStateKind.Durable ||
            !Accepts(item.Schema!, nominalSchemaId)) {
            throw new InvalidDataException($"Object ID {id} does not satisfy nominal Schema {nominalSchemaId} in this DTO view.");
        }
    }

    internal static bool Accepts(DurableSchema schema, string nominalSchemaId) {
        for (DurableSchema? current = schema; current is not null; current = current.BaseSchema) {
            if (StringComparer.Ordinal.Equals(current.SchemaId, nominalSchemaId)) {
                return true;
            }
        }
        return false;
    }
}
