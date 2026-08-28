using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

internal interface IObjectBehavior {
    int CurrentBasePayloadBytes { get; }

    UpdateObject GenerateUpdate(uint objectId, RandomStream random);
}

internal abstract class ObjectBehavior<TState, TMutation> : IObjectBehavior
    where TState : notnull {
    private TState _state;
    private int _currentBasePayloadBytes;

    protected ObjectBehavior(TState initialState, long initialBasePayloadBytes) {
        ArgumentNullException.ThrowIfNull(initialState);

        _state = initialState;
        _currentBasePayloadBytes = ValidateBasePayloadBytes(initialBasePayloadBytes);
    }

    public int CurrentBasePayloadBytes => _currentBasePayloadBytes;

    protected TState CurrentState => _state;

    public UpdateObject GenerateUpdate(uint objectId, RandomStream random) {
        ArgumentNullException.ThrowIfNull(random);

        TState isolatedState = CloneStateForUpdate(_state);
        ArgumentNullException.ThrowIfNull(isolatedState);
        ObjectBehaviorCandidate<TState, TMutation> candidate = ProposeUpdate(
            isolatedState,
            random);
        ArgumentNullException.ThrowIfNull(candidate.State);

        int resultBasePayloadBytes = ValidateBasePayloadBytes(MeasureBase(candidate.State));
        int deltaPayloadBytes = ValidateDeltaPayloadBytes(
            MeasureDelta(_state, candidate.Mutation, candidate.State),
            _currentBasePayloadBytes,
            resultBasePayloadBytes);

        UpdateObject frozenChange = new(
            objectId,
            resultBasePayloadBytes,
            deltaPayloadBytes);

        _state = candidate.State;
        _currentBasePayloadBytes = resultBasePayloadBytes;
        return frozenChange;
    }

    protected abstract TState CloneStateForUpdate(TState current);

    protected abstract ObjectBehaviorCandidate<TState, TMutation> ProposeUpdate(
        TState current,
        RandomStream random);

    protected abstract long MeasureBase(TState state);

    protected abstract long MeasureDelta(
        TState before,
        TMutation mutation,
        TState candidate);

    private static int ValidateBasePayloadBytes(long value) {
        int result = checked((int)value);
        if (result < 0) {
            throw new InvalidOperationException("A generated Base payload size cannot be negative.");
        }

        return result;
    }

    private static int ValidateDeltaPayloadBytes(
        long value,
        int previousBasePayloadBytes,
        int resultBasePayloadBytes) {
        int result = checked((int)value);
        if (result <= 0) {
            throw new InvalidOperationException("A generated Delta payload size must be positive.");
        }

        int growthBytes = Math.Max(0, resultBasePayloadBytes - previousBasePayloadBytes);
        if (result < growthBytes) {
            throw new InvalidOperationException(
                $"A generated Delta of {result} bytes cannot represent growth of {growthBytes} bytes.");
        }

        return result;
    }
}

internal readonly record struct ObjectBehaviorCandidate<TState, TMutation>(
    TState State,
    TMutation Mutation);
