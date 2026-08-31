using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads;

/// <summary>
/// One independently authored lane on a shared Save timeline. A channel may be
/// silent on a Save, but each ObjectId belongs to exactly one channel.
/// </summary>
internal sealed class WorkloadChannel {
    private readonly ReadOnlyCollection<ReadOnlyCollection<WorkloadChange>> _steps;

    public WorkloadChannel(
        string name,
        IEnumerable<IEnumerable<WorkloadChange>> steps) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A workload channel name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(steps);
        ReadOnlyCollection<WorkloadChange>[] snapshot = steps
            .Select(static changes => Array.AsReadOnly(
                (changes ?? throw new ArgumentException(
                    "A workload channel cannot contain a null step.",
                    nameof(steps)))
                .Select(static change => change ?? throw new ArgumentException(
                    "A workload channel cannot contain a null change.",
                    nameof(steps)))
                .ToArray()))
            .ToArray();
        if (snapshot.Length == 0) {
            throw new ArgumentException(
                "A workload channel must contain at least one timeline step.",
                nameof(steps));
        }

        Name = name;
        _steps = Array.AsReadOnly(snapshot);
    }

    public string Name { get; }

    public IReadOnlyList<ReadOnlyCollection<WorkloadChange>> Steps => _steps;

    public static WorkloadChannel FromTrace(string name, WorkloadTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);
        return new WorkloadChannel(name, trace.Steps.Select(static step => step.Changes));
    }
}

/// <summary>
/// Merges independent workload channels at each Save boundary. This is a trace
/// construction helper only; channels do not survive into evaluator semantics.
/// </summary>
internal static class WorkloadTraceComposer {
    public const string GeneratorId = "two-leg-rotation-probe/aligned-channels";
    public const int GeneratorVersion = 1;

    public static WorkloadTrace Compose(
        string scenarioName,
        ulong seed,
        IEnumerable<WorkloadChannel> channels) {
        ArgumentNullException.ThrowIfNull(channels);
        WorkloadChannel[] snapshot = channels
            .Select(static channel => channel ?? throw new ArgumentException(
                "A composite workload cannot contain a null channel.",
                nameof(channels)))
            .ToArray();
        if (snapshot.Length == 0) {
            throw new ArgumentException(
                "A composite workload requires at least one channel.",
                nameof(channels));
        }

        if (snapshot.Select(static channel => channel.Name)
            .Distinct(StringComparer.Ordinal).Count() != snapshot.Length) {
            throw new ArgumentException(
                "Composite workload channel names must be unique.",
                nameof(channels));
        }

        int stepCount = snapshot[0].Steps.Count;
        if (snapshot.Any(channel => channel.Steps.Count != stepCount)) {
            throw new ArgumentException(
                "Composite workload channels must share one aligned Save timeline.",
                nameof(channels));
        }

        ValidateObjectOwnership(snapshot, nameof(channels));
        SaveStep[] steps = Enumerable.Range(0, stepCount)
            .Select(stepIndex => new SaveStep(snapshot.SelectMany(
                channel => channel.Steps[stepIndex])))
            .ToArray();
        WorkloadTrace trace = new(
            scenarioName,
            GeneratorId,
            GeneratorVersion,
            seed,
            steps);
        _ = WorkloadReplayer.Replay(trace);
        return trace;
    }

    private static void ValidateObjectOwnership(
        IEnumerable<WorkloadChannel> channels,
        string parameterName) {
        Dictionary<uint, string> ownerByObjectId = [];
        foreach (WorkloadChannel channel in channels) {
            foreach (uint objectId in channel.Steps
                .SelectMany(static changes => changes)
                .Select(static change => change.ObjectId)
                .Distinct()) {
                if (ownerByObjectId.TryGetValue(objectId, out string? owner)) {
                    throw new ArgumentException(
                        $"Object {objectId} belongs to both workload channels " +
                        $"'{owner}' and '{channel.Name}'.",
                        parameterName);
                }

                ownerByObjectId.Add(objectId, channel.Name);
            }
        }
    }
}
