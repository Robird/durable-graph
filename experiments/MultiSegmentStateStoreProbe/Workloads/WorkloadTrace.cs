using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads;

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
            throw new ArgumentOutOfRangeException(nameof(generatorVersion));
        }

        ArgumentNullException.ThrowIfNull(steps);
        SaveStep[] snapshot = steps
            .Select(static step => step ?? throw new ArgumentException(
                "A workload trace cannot contain a null Save step.",
                nameof(steps)))
            .ToArray();
        if (snapshot.Length == 0) {
            throw new ArgumentException("A workload trace must contain at least one Save step.", nameof(steps));
        }

        ScenarioName = scenarioName;
        GeneratorId = generatorId;
        GeneratorVersion = generatorVersion;
        Seed = seed;
        _steps = Array.AsReadOnly(snapshot);
    }

    public string ScenarioName { get; }

    public string GeneratorId { get; }

    public int GeneratorVersion { get; }

    public ulong Seed { get; }

    public IReadOnlyList<SaveStep> Steps => _steps;
}
