namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

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
        if (componentCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(componentCount));
        }

        ValidateByteRange(
            initialComponentBytesMinInclusive,
            initialComponentBytesMaxExclusive,
            nameof(initialComponentBytesMaxExclusive));
        ValidateByteRange(
            replacementComponentBytesMinInclusive,
            replacementComponentBytesMaxExclusive,
            nameof(replacementComponentBytesMaxExclusive));

        if (maxReplacedComponentsPerUpdate <= 0
            || maxReplacedComponentsPerUpdate > componentCount) {
            throw new ArgumentOutOfRangeException(nameof(maxReplacedComponentsPerUpdate));
        }

        if (deltaOperationOverheadBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(deltaOperationOverheadBytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(deltaComponentOverheadBytes);

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

    private static void ValidateByteRange(int inclusiveMin, int exclusiveMax, string parameterName) {
        ArgumentOutOfRangeException.ThrowIfNegative(inclusiveMin);
        if (exclusiveMax <= inclusiveMin) {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

internal sealed class FieldObjectBehavior
    : ObjectBehavior<FieldObjectState, FieldMutation> {
    private readonly FieldBehaviorParameters _parameters;

    private FieldObjectBehavior(
        FieldBehaviorParameters parameters,
        FieldObjectState initialState)
        : base(initialState, MeasureBaseCore(initialState)) {
        _parameters = parameters;
    }

    public static FieldObjectBehavior Create(
        FieldBehaviorParameters parameters,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);

        int[] componentBytes = new int[parameters.ComponentCount];
        for (int index = 0; index < componentBytes.Length; index++) {
            componentBytes[index] = random.NextInt(
                parameters.InitialComponentBytesMinInclusive,
                parameters.InitialComponentBytesMaxExclusive);
        }

        return new FieldObjectBehavior(parameters, new FieldObjectState(componentBytes));
    }

    protected override FieldObjectState CloneStateForUpdate(FieldObjectState current) {
        return new FieldObjectState((int[])current.ComponentBytes.Clone());
    }

    protected override ObjectBehaviorCandidate<FieldObjectState, FieldMutation> ProposeUpdate(
        FieldObjectState current,
        RandomStream random) {
        int replaceCount = random.NextInt(_parameters.MaxReplacedComponentsPerUpdate) + 1;
        int[] candidateComponentBytes = current.ComponentBytes;
        int[] candidateIndexes = Enumerable.Range(0, candidateComponentBytes.Length).ToArray();
        ShufflePrefix(candidateIndexes, replaceCount, random);

        FieldReplacement[] replacements = new FieldReplacement[replaceCount];
        for (int index = 0; index < replaceCount; index++) {
            int componentIndex = candidateIndexes[index];
            int replacementBytes = random.NextInt(
                _parameters.ReplacementComponentBytesMinInclusive,
                _parameters.ReplacementComponentBytesMaxExclusive);
            candidateComponentBytes[componentIndex] = replacementBytes;
            replacements[index] = new FieldReplacement(componentIndex, replacementBytes);
        }

        return new ObjectBehaviorCandidate<FieldObjectState, FieldMutation>(
            new FieldObjectState(candidateComponentBytes),
            new FieldMutation(replacements));
    }

    protected override long MeasureBase(FieldObjectState state) => MeasureBaseCore(state);

    protected override long MeasureDelta(
        FieldObjectState before,
        FieldMutation mutation,
        FieldObjectState candidate) {
        long result = _parameters.DeltaOperationOverheadBytes;
        foreach (FieldReplacement replacement in mutation.Replacements) {
            result = checked(result + _parameters.DeltaComponentOverheadBytes);
            result = checked(result + replacement.ReplacementBytes);
        }

        return result;
    }

    private static long MeasureBaseCore(FieldObjectState state) {
        long result = 0;
        foreach (int componentBytes in state.ComponentBytes) {
            result = checked(result + componentBytes);
        }

        return result;
    }

    private static void ShufflePrefix(int[] indexes, int prefixLength, RandomStream random) {
        for (int index = 0; index < prefixLength; index++) {
            int selectedIndex = random.NextInt(index, indexes.Length);
            (indexes[index], indexes[selectedIndex]) = (indexes[selectedIndex], indexes[index]);
        }
    }
}

internal sealed record FieldObjectState(int[] ComponentBytes);

internal sealed record FieldMutation(IReadOnlyList<FieldReplacement> Replacements);

internal readonly record struct FieldReplacement(
    int ComponentIndex,
    int ReplacementBytes);
