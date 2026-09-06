namespace Atelia.DurableGraph;

/// <summary>The in-memory content kind of a captured object; not a wire-format type code.</summary>
public enum CapturedObjectKind {
    Durable,
    String,
}

/// <summary>One immutable object record. Domain instances are never exposed or retained here.</summary>
public sealed class CapturedObject {
    private readonly object _content;

    internal CapturedObject(uint id, DurableSchema schema, object state) {
        Id = id;
        Kind = CapturedObjectKind.Durable;
        Schema = schema;
        _content = state;
    }

    internal CapturedObject(uint id, string content) {
        Id = id;
        Kind = CapturedObjectKind.String;
        _content = content;
    }

    public uint Id { get; }
    public CapturedObjectKind Kind { get; }
    public DurableSchema? Schema { get; }

    /// <summary>Returns a copy of the exact captured DTO, never the stored box.</summary>
    public TState GetState<TState>() where TState : unmanaged {
        if (Kind != CapturedObjectKind.Durable || _content is not TState state) {
            throw new InvalidOperationException("The record does not contain the requested durable DTO type.");
        }
        return state;
    }

    public string StringContent => Kind == CapturedObjectKind.String
        ? (string)_content
        : throw new InvalidOperationException("The record is not a string.");
}
