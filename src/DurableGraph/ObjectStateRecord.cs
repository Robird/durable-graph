namespace Atelia.DurableGraph;

/// <summary>The in-memory content kind of a captured or decoded object; not a wire-format type code.</summary>
public enum ObjectStateKind {
    Durable,
    String,
}

/// <summary>
/// One immutable captured or decoded object state record. Domain instances are never exposed or retained here.
/// A decoded record alone is not a capture candidate or an accepted session baseline.
/// </summary>
public sealed class ObjectStateRecord {
    private readonly object _content;

    internal ObjectStateRecord(ObjectId id, DurableSchema schema, object state, ICapturedStatePreparation? preparation = null) {
        ArgumentNullException.ThrowIfNull(schema);
        schema.RequireReferenceObject();
        Id = id;
        Kind = ObjectStateKind.Durable;
        Schema = schema;
        _content = state;
        Preparation = preparation;
    }

    internal ObjectStateRecord(ObjectId id, string content) {
        Id = id;
        Kind = ObjectStateKind.String;
        _content = content;
    }

    public ObjectId Id { get; }
    public ObjectStateKind Kind { get; }
    public DurableSchema? Schema { get; }
    internal ICapturedStatePreparation? Preparation { get; }

    /// <summary>Returns a copy of the exact DTO, never the stored box.</summary>
    public TState GetState<TState>() where TState : unmanaged {
        if (Kind != ObjectStateKind.Durable || _content is not TState state) {
            throw new InvalidOperationException("The record does not contain the requested durable DTO type.");
        }
        return state;
    }

    public string StringContent => Kind == ObjectStateKind.String
        ? (string)_content
        : throw new InvalidOperationException("The record is not a string.");
}
