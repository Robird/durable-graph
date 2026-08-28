using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads;

internal sealed class WorkloadTrace {
    private readonly ReadOnlyCollection<SaveStep> _steps;

    public WorkloadTrace(
        string scenarioName,
        string generatorId,
        int generatorVersion,
        ulong seed,
        IEnumerable<SaveStep> steps) {
        if (string.IsNullOrWhiteSpace(scenarioName)) {
            throw new ArgumentException("A scenario name is required.", nameof(scenarioName));
        }

        if (string.IsNullOrWhiteSpace(generatorId)) {
            throw new ArgumentException("A generator identifier is required.", nameof(generatorId));
        }

        if (generatorVersion <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(generatorVersion),
                generatorVersion,
                "A generator version must be positive.");
        }

        ArgumentNullException.ThrowIfNull(steps);
        SaveStep[] stepSnapshot = steps
            .Select(static step => step ?? throw new ArgumentException(
                "A workload trace cannot contain a null save step.",
                nameof(steps)))
            .ToArray();

        if (stepSnapshot.Length == 0) {
            throw new ArgumentException("A workload trace must contain at least one save step.", nameof(steps));
        }

        ScenarioName = scenarioName;
        GeneratorId = generatorId;
        GeneratorVersion = generatorVersion;
        Seed = seed;
        _steps = Array.AsReadOnly(stepSnapshot);
    }

    public string ScenarioName { get; }

    public string GeneratorId { get; }

    public int GeneratorVersion { get; }

    public ulong Seed { get; }

    public IReadOnlyList<SaveStep> Steps => _steps;
}
