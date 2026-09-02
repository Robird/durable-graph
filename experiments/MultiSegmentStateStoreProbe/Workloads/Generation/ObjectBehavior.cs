using Atelia.MultiSegmentStateStoreProbe.Workloads;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal interface IObjectBehavior {
    int CurrentBasePayloadBytes { get; }

    UpdateObject GenerateUpdate(uint objectId, RandomStream random);
}

internal abstract class ObjectBehavior<TState, TMutation> : IObjectBehavior
    where TState : notnull {
    private TState _state;
    private int _currentBasePayloadBytes;

    protected ObjectBehavior(TState initialState, long initialBasePayloadBytes) {
        _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _currentBasePayloadBytes = ValidateBase(initialBasePayloadBytes);
    }

    public int CurrentBasePayloadBytes => _currentBasePayloadBytes;

    protected TState CurrentState => _state;

    public UpdateObject GenerateUpdate(uint objectId, RandomStream random) {
        ArgumentNullException.ThrowIfNull(random);
        TState isolated = CloneStateForUpdate(_state);
        ObjectBehaviorCandidate<TState, TMutation> candidate = ProposeUpdate(isolated, random);
        int resultBase = ValidateBase(MeasureBase(candidate.State));
        int resultDelta = ValidateDelta(
            MeasureDelta(_state, candidate.Mutation, candidate.State),
            _currentBasePayloadBytes,
            resultBase);

        UpdateObject result = new(objectId, resultBase, resultDelta);
        _state = candidate.State;
        _currentBasePayloadBytes = resultBase;
        return result;
    }

    protected abstract TState CloneStateForUpdate(TState current);
    protected abstract ObjectBehaviorCandidate<TState, TMutation> ProposeUpdate(
        TState current,
        RandomStream random);
    protected abstract long MeasureBase(TState state);
    protected abstract long MeasureDelta(TState before, TMutation mutation, TState candidate);

    private static int ValidateBase(long value) {
        int result = checked((int)value);
        if (result < 0) {
            throw new InvalidOperationException("A generated Base size cannot be negative.");
        }

        return result;
    }

    private static int ValidateDelta(long value, int previousBase, int resultBase) {
        int result = checked((int)value);
        if (result <= 0 || result < Math.Max(0, resultBase - previousBase)) {
            throw new InvalidOperationException("A generated Delta cannot represent its state change.");
        }

        return result;
    }
}

internal readonly record struct ObjectBehaviorCandidate<TState, TMutation>(
    TState State,
    TMutation Mutation);
