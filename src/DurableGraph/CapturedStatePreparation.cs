using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Prepares an owned Base body from a frozen DTO.</summary>
public delegate PreparedBase StateBasePreparer<TState>(in TState state) where TState : unmanaged;

/// <summary>Prepares one fused change decision and Delta body from frozen DTOs.</summary>
public delegate PreparedDelta StateDeltaPreparer<TState>(in TState prior, in TState current) where TState : unmanaged;

/// <summary>Pairs an exact Schema and DTO with its statically bound body operations.</summary>
/// <remarks>
/// Reuse one stable instance for a captured type. Callbacks must be deterministic and must
/// not retain mutable domain state. This binding supplies operations, not persistent type authority.
/// </remarks>
public sealed class CapturedStatePreparation<TState> : ICapturedStatePreparation where TState : unmanaged {
    private readonly StateBasePreparer<TState> _prepareBase;
    private readonly StateDeltaPreparer<TState> _prepareDelta;

    public CapturedStatePreparation(
        DurableSchema schema,
        StateBasePreparer<TState> prepareBase,
        StateDeltaPreparer<TState> prepareDelta) {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(prepareBase);
        ArgumentNullException.ThrowIfNull(prepareDelta);
        Schema = schema;
        _prepareBase = prepareBase;
        _prepareDelta = prepareDelta;
    }

    public DurableSchema Schema { get; }

    void ICapturedStatePreparation.Validate(CapturedObject item) {
        if (item.Kind != CapturedObjectKind.Durable || !Schema.Equals(item.Schema)) {
            throw new InvalidOperationException("The preparation binding requires its exact durable Schema.");
        }
        _ = item.GetState<TState>();
    }

    PreparedBase ICapturedStatePreparation.PrepareBase(CapturedObject current) {
        TState state = current.GetState<TState>();
        return _prepareBase(in state) ?? throw new InvalidOperationException("Base preparation returned null.");
    }

    PreparedDelta ICapturedStatePreparation.PrepareDelta(CapturedObject previous, CapturedObject current) {
        TState prior = previous.GetState<TState>();
        TState state = current.GetState<TState>();
        return _prepareDelta(in prior, in state) ?? throw new InvalidOperationException("Delta preparation returned null.");
    }
}

internal interface ICapturedStatePreparation {
    void Validate(CapturedObject item);
    PreparedBase PrepareBase(CapturedObject current);
    PreparedDelta PrepareDelta(CapturedObject previous, CapturedObject current);
}
