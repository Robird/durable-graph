namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal sealed record ListBehaviorParameters {
    public ListBehaviorParameters(
        int initialItemCount,
        int initialItemBytesMinInclusive,
        int initialItemBytesMaxExclusive,
        int insertedItemBytesMinInclusive,
        int insertedItemBytesMaxExclusive,
        int replacementItemBytesMinInclusive,
        int replacementItemBytesMaxExclusive,
        int deltaOperationOverheadBytes,
        int insertWeight,
        int removeWeight,
        int replaceWeight) {
        if (initialItemCount < 0 ||
            initialItemBytesMinInclusive < 0 ||
            initialItemBytesMaxExclusive <= initialItemBytesMinInclusive ||
            insertedItemBytesMinInclusive < 0 ||
            insertedItemBytesMaxExclusive <= insertedItemBytesMinInclusive ||
            replacementItemBytesMinInclusive < 0 ||
            replacementItemBytesMaxExclusive <= replacementItemBytesMinInclusive ||
            deltaOperationOverheadBytes <= 0 ||
            insertWeight <= 0 || removeWeight < 0 || replaceWeight < 0) {
            throw new ArgumentOutOfRangeException(nameof(initialItemCount));
        }

        long totalWeight = checked((long)insertWeight + removeWeight + replaceWeight);
        if (totalWeight > int.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(replaceWeight));
        }

        InitialItemCount = initialItemCount;
        InitialItemBytesMinInclusive = initialItemBytesMinInclusive;
        InitialItemBytesMaxExclusive = initialItemBytesMaxExclusive;
        InsertedItemBytesMinInclusive = insertedItemBytesMinInclusive;
        InsertedItemBytesMaxExclusive = insertedItemBytesMaxExclusive;
        ReplacementItemBytesMinInclusive = replacementItemBytesMinInclusive;
        ReplacementItemBytesMaxExclusive = replacementItemBytesMaxExclusive;
        DeltaOperationOverheadBytes = deltaOperationOverheadBytes;
        InsertWeight = insertWeight;
        RemoveWeight = removeWeight;
        ReplaceWeight = replaceWeight;
        TotalWeight = checked((int)totalWeight);
    }

    public int InitialItemCount { get; }
    public int InitialItemBytesMinInclusive { get; }
    public int InitialItemBytesMaxExclusive { get; }
    public int InsertedItemBytesMinInclusive { get; }
    public int InsertedItemBytesMaxExclusive { get; }
    public int ReplacementItemBytesMinInclusive { get; }
    public int ReplacementItemBytesMaxExclusive { get; }
    public int DeltaOperationOverheadBytes { get; }
    public int InsertWeight { get; }
    public int RemoveWeight { get; }
    public int ReplaceWeight { get; }
    public int TotalWeight { get; }
}

internal sealed class ListObjectBehavior : ObjectBehavior<int[], ListMutation> {
    private readonly ListBehaviorParameters _parameters;

    private ListObjectBehavior(ListBehaviorParameters parameters, int[] initial)
        : base(initial, initial.Sum(static value => (long)value)) {
        _parameters = parameters;
    }

    public int CurrentItemCount => CurrentState.Length;

    public static ListObjectBehavior Create(
        ListBehaviorParameters parameters,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);
        int[] items = new int[parameters.InitialItemCount];
        for (int index = 0; index < items.Length; index++) {
            items[index] = random.NextInt(
                parameters.InitialItemBytesMinInclusive,
                parameters.InitialItemBytesMaxExclusive);
        }

        return new ListObjectBehavior(parameters, items);
    }

    protected override int[] CloneStateForUpdate(int[] current) => (int[])current.Clone();

    protected override ObjectBehaviorCandidate<int[], ListMutation> ProposeUpdate(
        int[] current,
        RandomStream random) {
        ListMutationKind kind = SelectKind(current, random);
        return kind switch {
            ListMutationKind.Insert => Insert(current, random),
            ListMutationKind.Remove => Remove(current, random),
            ListMutationKind.Replace => Replace(current, random),
            _ => throw new InvalidOperationException(),
        };
    }

    protected override long MeasureBase(int[] state) =>
        state.Sum(static value => (long)value);

    protected override long MeasureDelta(
        int[] before,
        ListMutation mutation,
        int[] candidate) => mutation.Kind is ListMutationKind.Insert or ListMutationKind.Replace
        ? checked((long)_parameters.DeltaOperationOverheadBytes + mutation.ContentBytes)
        : _parameters.DeltaOperationOverheadBytes;

    private ListMutationKind SelectKind(int[] current, RandomStream random) {
        if (current.Length == 0) {
            return ListMutationKind.Insert;
        }

        int selection = random.NextInt(_parameters.TotalWeight);
        if (selection < _parameters.InsertWeight) {
            return ListMutationKind.Insert;
        }

        selection -= _parameters.InsertWeight;
        return selection < _parameters.RemoveWeight
            ? ListMutationKind.Remove
            : ListMutationKind.Replace;
    }

    private ObjectBehaviorCandidate<int[], ListMutation> Insert(
        int[] current,
        RandomStream random) {
        int index = random.NextInt(current.Length + 1);
        int bytes = random.NextInt(
            _parameters.InsertedItemBytesMinInclusive,
            _parameters.InsertedItemBytesMaxExclusive);
        int[] candidate = new int[current.Length + 1];
        Array.Copy(current, 0, candidate, 0, index);
        candidate[index] = bytes;
        Array.Copy(current, index, candidate, index + 1, current.Length - index);
        return new(candidate, new ListMutation(ListMutationKind.Insert, bytes));
    }

    private static ObjectBehaviorCandidate<int[], ListMutation> Remove(
        int[] current,
        RandomStream random) {
        int index = random.NextInt(current.Length);
        int[] candidate = new int[current.Length - 1];
        Array.Copy(current, 0, candidate, 0, index);
        Array.Copy(current, index + 1, candidate, index, current.Length - index - 1);
        return new(candidate, new ListMutation(ListMutationKind.Remove, 0));
    }

    private ObjectBehaviorCandidate<int[], ListMutation> Replace(
        int[] current,
        RandomStream random) {
        int index = random.NextInt(current.Length);
        int bytes = random.NextInt(
            _parameters.ReplacementItemBytesMinInclusive,
            _parameters.ReplacementItemBytesMaxExclusive);
        current[index] = bytes;
        return new(current, new ListMutation(ListMutationKind.Replace, bytes));
    }
}

internal readonly record struct ListMutation(ListMutationKind Kind, int ContentBytes);

internal enum ListMutationKind {
    Insert,
    Remove,
    Replace,
}
