namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

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

        if (stepCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }

        if (initialPopulation <= 0) {
            throw new ArgumentOutOfRangeException(nameof(initialPopulation));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(createPerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUpdatePerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRemovePerLaterStep);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldObjectWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(listObjectWeight);

        long totalObjectWeight = checked((long)fieldObjectWeight + listObjectWeight);
        if (totalObjectWeight <= 0 || totalObjectWeight > int.MaxValue) {
            throw new ArgumentOutOfRangeException(
                nameof(listObjectWeight),
                "The combined object weights must fit in a positive Int32 value.");
        }

        ArgumentNullException.ThrowIfNull(fieldBehavior);
        ArgumentNullException.ThrowIfNull(listBehavior);

        long maximumObjectCount = checked(
            (long)initialPopulation + ((long)stepCount - 1) * createPerLaterStep);
        if (maximumObjectCount > uint.MaxValue) {
            throw new ArgumentOutOfRangeException(
                nameof(createPerLaterStep),
                "The scenario could exhaust the UInt32 object id range.");
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
        FieldBehavior = fieldBehavior;
        ListBehavior = listBehavior;
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
