namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

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
        ArgumentOutOfRangeException.ThrowIfNegative(initialItemCount);
        ValidateByteRange(
            initialItemBytesMinInclusive,
            initialItemBytesMaxExclusive,
            nameof(initialItemBytesMaxExclusive));
        ValidateByteRange(
            insertedItemBytesMinInclusive,
            insertedItemBytesMaxExclusive,
            nameof(insertedItemBytesMaxExclusive));
        ValidateByteRange(
            replacementItemBytesMinInclusive,
            replacementItemBytesMaxExclusive,
            nameof(replacementItemBytesMaxExclusive));
        if (deltaOperationOverheadBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(deltaOperationOverheadBytes));
        }

        if (insertWeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(insertWeight));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(removeWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(replaceWeight);
        long totalMutationWeight = checked((long)insertWeight + removeWeight + replaceWeight);
        if (totalMutationWeight > int.MaxValue) {
            throw new ArgumentOutOfRangeException(
                nameof(replaceWeight),
                "The combined List mutation weights must fit in a positive Int32 value.");
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
        TotalMutationWeight = (int)totalMutationWeight;
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

    public int TotalMutationWeight { get; }

    private static void ValidateByteRange(int inclusiveMin, int exclusiveMax, string parameterName) {
        ArgumentOutOfRangeException.ThrowIfNegative(inclusiveMin);
        if (exclusiveMax <= inclusiveMin) {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

internal sealed class ListObjectBehavior
    : ObjectBehavior<ListObjectState, ListMutation> {
    private readonly ListBehaviorParameters _parameters;

    private ListObjectBehavior(
        ListBehaviorParameters parameters,
        ListObjectState initialState)
        : base(initialState, MeasureBaseCore(initialState)) {
        _parameters = parameters;
    }

    public int CurrentItemCount => CurrentState.ItemBytes.Length;

    public static ListObjectBehavior Create(
        ListBehaviorParameters parameters,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);

        int[] itemBytes = new int[parameters.InitialItemCount];
        for (int index = 0; index < itemBytes.Length; index++) {
            itemBytes[index] = random.NextInt(
                parameters.InitialItemBytesMinInclusive,
                parameters.InitialItemBytesMaxExclusive);
        }

        return new ListObjectBehavior(parameters, new ListObjectState(itemBytes));
    }

    protected override ListObjectState CloneStateForUpdate(ListObjectState current) {
        return new ListObjectState((int[])current.ItemBytes.Clone());
    }

    protected override ObjectBehaviorCandidate<ListObjectState, ListMutation> ProposeUpdate(
        ListObjectState current,
        RandomStream random) {
        ListMutationKind kind = SelectMutationKind(current, random);

        ListMutation mutation;
        int[] candidate;
        switch (kind) {
            case ListMutationKind.Insert:
                (candidate, mutation) = ProposeInsert(current, random);
                break;
            case ListMutationKind.Remove:
                (candidate, mutation) = ProposeRemove(current, random);
                break;
            case ListMutationKind.Replace:
                (candidate, mutation) = ProposeReplace(current, random);
                break;
            default:
                throw new InvalidOperationException($"Unsupported List mutation kind '{kind}'.");
        }

        return new ObjectBehaviorCandidate<ListObjectState, ListMutation>(
            new ListObjectState(candidate),
            mutation);
    }

    private ListMutationKind SelectMutationKind(
        ListObjectState current,
        RandomStream random) {
        if (current.ItemBytes.Length == 0) {
            return ListMutationKind.Insert;
        }

        int selection = random.NextInt(_parameters.TotalMutationWeight);
        if (selection < _parameters.InsertWeight) {
            return ListMutationKind.Insert;
        }

        selection -= _parameters.InsertWeight;
        if (selection < _parameters.RemoveWeight) {
            return ListMutationKind.Remove;
        }

        return ListMutationKind.Replace;
    }

    protected override long MeasureBase(ListObjectState state) => MeasureBaseCore(state);

    protected override long MeasureDelta(
        ListObjectState before,
        ListMutation mutation,
        ListObjectState candidate) {
        return mutation.Kind switch {
            ListMutationKind.Insert or ListMutationKind.Replace =>
                checked((long)_parameters.DeltaOperationOverheadBytes + mutation.ContentBytes),
            ListMutationKind.Remove => _parameters.DeltaOperationOverheadBytes,
            _ => throw new InvalidOperationException(
                $"Unsupported List mutation kind '{mutation.Kind}'."),
        };
    }

    private (int[] Candidate, ListMutation Mutation) ProposeInsert(
        ListObjectState current,
        RandomStream random) {
        int insertionIndex = random.NextInt(current.ItemBytes.Length + 1);
        int insertedBytes = random.NextInt(
            _parameters.InsertedItemBytesMinInclusive,
            _parameters.InsertedItemBytesMaxExclusive);
        int[] candidate = new int[current.ItemBytes.Length + 1];
        Array.Copy(current.ItemBytes, 0, candidate, 0, insertionIndex);
        candidate[insertionIndex] = insertedBytes;
        Array.Copy(
            current.ItemBytes,
            insertionIndex,
            candidate,
            insertionIndex + 1,
            current.ItemBytes.Length - insertionIndex);
        return (candidate, new ListMutation(ListMutationKind.Insert, insertedBytes));
    }

    private static (int[] Candidate, ListMutation Mutation) ProposeRemove(
        ListObjectState current,
        RandomStream random) {
        int removalIndex = random.NextInt(current.ItemBytes.Length);
        int[] candidate = new int[current.ItemBytes.Length - 1];
        Array.Copy(current.ItemBytes, 0, candidate, 0, removalIndex);
        Array.Copy(
            current.ItemBytes,
            removalIndex + 1,
            candidate,
            removalIndex,
            current.ItemBytes.Length - removalIndex - 1);
        return (candidate, new ListMutation(ListMutationKind.Remove, ContentBytes: 0));
    }

    private (int[] Candidate, ListMutation Mutation) ProposeReplace(
        ListObjectState current,
        RandomStream random) {
        int replacementIndex = random.NextInt(current.ItemBytes.Length);
        int replacementBytes = random.NextInt(
            _parameters.ReplacementItemBytesMinInclusive,
            _parameters.ReplacementItemBytesMaxExclusive);
        int[] candidate = current.ItemBytes;
        candidate[replacementIndex] = replacementBytes;
        return (candidate, new ListMutation(ListMutationKind.Replace, replacementBytes));
    }

    private static long MeasureBaseCore(ListObjectState state) {
        long result = 0;
        foreach (int itemBytes in state.ItemBytes) {
            result = checked(result + itemBytes);
        }

        return result;
    }
}

internal sealed record ListObjectState(int[] ItemBytes);

internal readonly record struct ListMutation(
    ListMutationKind Kind,
    int ContentBytes);

internal enum ListMutationKind {
    Insert,
    Remove,
    Replace,
}
