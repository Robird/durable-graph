namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal sealed record ScenarioDefinition {
    public ScenarioDefinition(
        string name,
        ulong seed,
        int stepCount,
        int initialPopulation,
        int createPerLaterStep,
        int maxUpdatePerLaterStep,
        int maxRemovePerLaterStep,
        int fieldObjectWeight,
        int listObjectWeight,
        FieldBehaviorParameters fieldBehavior,
        ListBehaviorParameters listBehavior) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A scenario name is required.", nameof(name));
        }

        if (stepCount <= 0 || initialPopulation <= 0) {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(createPerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUpdatePerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRemovePerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldObjectWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(listObjectWeight);
        long totalWeight = checked((long)fieldObjectWeight + listObjectWeight);
        if (totalWeight <= 0 || totalWeight > int.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(listObjectWeight));
        }

        long maximumObjects = checked(
            (long)initialPopulation + ((long)stepCount - 1) * createPerLaterStep);
        if (maximumObjects > uint.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(createPerLaterStep));
        }

        Name = name;
        Seed = seed;
        StepCount = stepCount;
        InitialPopulation = initialPopulation;
        CreatePerLaterStep = createPerLaterStep;
        MaxUpdatePerLaterStep = maxUpdatePerLaterStep;
        MaxRemovePerLaterStep = maxRemovePerLaterStep;
        FieldObjectWeight = fieldObjectWeight;
        ListObjectWeight = listObjectWeight;
        FieldBehavior = fieldBehavior ?? throw new ArgumentNullException(nameof(fieldBehavior));
        ListBehavior = listBehavior ?? throw new ArgumentNullException(nameof(listBehavior));
    }

    public string Name { get; }
    public ulong Seed { get; }
    public int StepCount { get; }
    public int InitialPopulation { get; }
    public int CreatePerLaterStep { get; }
    public int MaxUpdatePerLaterStep { get; }
    public int MaxRemovePerLaterStep { get; }
    public int FieldObjectWeight { get; }
    public int ListObjectWeight { get; }
    public FieldBehaviorParameters FieldBehavior { get; }
    public ListBehaviorParameters ListBehavior { get; }
}
