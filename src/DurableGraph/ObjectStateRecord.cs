namespace Atelia.DurableGraph;

/// <summary>The in-memory content kind of a captured or decoded object; not a wire-format type code.</summary>
public enum ObjectStateKind {
    Durable,
    String,
    Array,
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
        Layout = ObjectLayout.ForDurable(schema);
        _content = state;
        Preparation = preparation;
    }

    internal ObjectStateRecord(ObjectId id, string content) {
        Id = id;
        ArgumentNullException.ThrowIfNull(content);
        Layout = ObjectLayout.String;
        _content = content.Length == 0 ? string.Empty : content;
    }

    internal ObjectStateRecord(ObjectId id, ArrayLayout layout, object state, ICapturedStatePreparation? preparation = null) {
        ArgumentNullException.ThrowIfNull(state);
        Id = id;
        Layout = ObjectLayout.ForArray(layout);
        _content = state;
        Preparation = preparation;
    }

    public ObjectId Id { get; }
    public ObjectLayout Layout { get; }
    public ObjectStateKind Kind => Layout.Kind;
    public DurableSchema? Schema => Layout.Schema;
    internal object Content => _content;
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

    /// <summary>Returns the immutable exact array state, without exposing a writable buffer.</summary>
    public FrozenArrayState<TState> GetArrayState<TState>() where TState : unmanaged =>
        Kind == ObjectStateKind.Array && _content is FrozenArrayState<TState> state
            ? state : throw new InvalidOperationException("The record does not contain the requested exact array state type.");
}
