namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal sealed record FieldBehaviorParameters {
    public FieldBehaviorParameters(
        int componentCount,
        int initialComponentBytesMinInclusive,
        int initialComponentBytesMaxExclusive,
        int replacementComponentBytesMinInclusive,
        int replacementComponentBytesMaxExclusive,
        int maxReplacedComponentsPerUpdate,
        int deltaOperationOverheadBytes,
        int deltaComponentOverheadBytes) {
        if (componentCount <= 0 ||
            initialComponentBytesMinInclusive < 0 ||
            initialComponentBytesMaxExclusive <= initialComponentBytesMinInclusive ||
            replacementComponentBytesMinInclusive < 0 ||
            replacementComponentBytesMaxExclusive <= replacementComponentBytesMinInclusive ||
            maxReplacedComponentsPerUpdate <= 0 ||
            maxReplacedComponentsPerUpdate > componentCount ||
            deltaOperationOverheadBytes <= 0 ||
            deltaComponentOverheadBytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(componentCount));
        }

        ComponentCount = componentCount;
        InitialComponentBytesMinInclusive = initialComponentBytesMinInclusive;
        InitialComponentBytesMaxExclusive = initialComponentBytesMaxExclusive;
        ReplacementComponentBytesMinInclusive = replacementComponentBytesMinInclusive;
        ReplacementComponentBytesMaxExclusive = replacementComponentBytesMaxExclusive;
        MaxReplacedComponentsPerUpdate = maxReplacedComponentsPerUpdate;
        DeltaOperationOverheadBytes = deltaOperationOverheadBytes;
        DeltaComponentOverheadBytes = deltaComponentOverheadBytes;
    }

    public int ComponentCount { get; }
    public int InitialComponentBytesMinInclusive { get; }
    public int InitialComponentBytesMaxExclusive { get; }
    public int ReplacementComponentBytesMinInclusive { get; }
    public int ReplacementComponentBytesMaxExclusive { get; }
    public int MaxReplacedComponentsPerUpdate { get; }
    public int DeltaOperationOverheadBytes { get; }
    public int DeltaComponentOverheadBytes { get; }
}

internal sealed class FieldObjectBehavior : ObjectBehavior<int[], FieldMutation> {
    private readonly FieldBehaviorParameters _parameters;

    private FieldObjectBehavior(FieldBehaviorParameters parameters, int[] initial)
        : base(initial, initial.Sum(static value => (long)value)) {
        _parameters = parameters;
    }

    public static FieldObjectBehavior Create(
        FieldBehaviorParameters parameters,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);
        int[] values = new int[parameters.ComponentCount];
        for (int index = 0; index < values.Length; index++) {
            values[index] = random.NextInt(
                parameters.InitialComponentBytesMinInclusive,
                parameters.InitialComponentBytesMaxExclusive);
        }

        return new FieldObjectBehavior(parameters, values);
    }

    protected override int[] CloneStateForUpdate(int[] current) => (int[])current.Clone();

    protected override ObjectBehaviorCandidate<int[], FieldMutation> ProposeUpdate(
        int[] current,
        RandomStream random) {
        int count = random.NextInt(_parameters.MaxReplacedComponentsPerUpdate) + 1;
        int[] indexes = Enumerable.Range(0, current.Length).ToArray();
        for (int index = 0; index < count; index++) {
            int selected = random.NextInt(index, indexes.Length);
            (indexes[index], indexes[selected]) = (indexes[selected], indexes[index]);
        }

        int contentBytes = 0;
        for (int index = 0; index < count; index++) {
            int replacement = random.NextInt(
                _parameters.ReplacementComponentBytesMinInclusive,
                _parameters.ReplacementComponentBytesMaxExclusive);
            current[indexes[index]] = replacement;
            contentBytes = checked(contentBytes + replacement);
        }

        return new ObjectBehaviorCandidate<int[], FieldMutation>(
            current,
            new FieldMutation(count, contentBytes));
    }

    protected override long MeasureBase(int[] state) =>
        state.Sum(static value => (long)value);

    protected override long MeasureDelta(
        int[] before,
        FieldMutation mutation,
        int[] candidate) => checked(
        (long)_parameters.DeltaOperationOverheadBytes +
        (long)_parameters.DeltaComponentOverheadBytes * mutation.ReplacementCount +
        mutation.ContentBytes);
}

internal readonly record struct FieldMutation(int ReplacementCount, int ContentBytes);
