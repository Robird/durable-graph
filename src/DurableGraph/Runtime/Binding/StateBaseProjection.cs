using Atelia.DurableGraph.Schema;

namespace Atelia.DurableGraph.Runtime;

/// <summary>Captures and restores a base declaration's complete state on an existing compatible instance.</summary>
/// <remarks>
/// Generated infrastructure bound by <see cref="StateBindingContext"/>. This capability does not allocate,
/// normalize or register the projected instance; references in its fields use the supplied operation context.
/// Hydration is intended only for unpublished instances during graph restoration.
/// </remarks>
public sealed class StateBaseProjection<TBase, TState>
    where TBase : class, IDurableObject where TState : unmanaged {
    private readonly Func<TBase, CaptureContext, TState> _capture;
    private readonly StateHydrator<TBase, TState> _hydrate;

    internal StateBaseProjection(Func<TBase, CaptureContext, TState> capture, StateHydrator<TBase, TState> hydrate) {
        _capture = capture;
        _hydrate = hydrate;
    }

    public TState Capture(TBase value, CaptureContext context) {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);
        return _capture(value, context);
    }

    public void Hydrate(TBase target, in TState state, ObjectReadTable objects) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(objects);
        _hydrate(target, in state, objects);
    }
}

public abstract partial class StateBindingContext {
    /// <summary>Binds explicitly supported current base projection after validating its complete exact layout.</summary>
    public StateBaseProjection<TBase, TState> BindBaseProjection<TBase, TState>(DurableSchema expectedBaseSchema)
        where TBase : class, IDurableObject where TState : unmanaged {
        ArgumentNullException.ThrowIfNull(expectedBaseSchema);
        StateModelBinding model = ResolveCurrentModel(typeof(TBase));
        if (model is not StateModelBinding<TBase, TState> typed || !model.CurrentSchema.Equals(expectedBaseSchema)) {
            throw new InvalidDataException("Base projection requires the exact current domain type, DTO type and complete Schema.");
        }
        // BindSchema also rechecks late repository registrations on a cached layout.
        BindSchema(expectedBaseSchema);
        return typed.CreateBaseProjection();
    }
}
