namespace Atelia.DurableGraph.Runtime;

/// <summary>An unmanaged frozen optional value. Its default value is absent.</summary>
/// <remarks>Only a present value contains persistent child state; absent state has a default payload.</remarks>
public readonly struct NullableState<TState> where TState : unmanaged {
    public NullableState(TState value) {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; }

    /// <summary>Gets the child state, or its default value when absent.</summary>
    public TState Value { get; }
}
