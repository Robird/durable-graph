using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class SimulationRun {
    private readonly ReadOnlyDictionary<uint, AbsoluteFrameAddress> _stateMap;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _revisionAddresses;
    private readonly ReadOnlyCollection<RevisionObservation> _observations;

    internal SimulationRun(
        WorkloadTrace sourceTrace,
        BaselinePolicy policy,
        RbfFileStore fileStore,
        uint currentFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        IEnumerable<AbsoluteFrameAddress> revisionAddresses,
        IEnumerable<RevisionObservation> observations) {
        ArgumentNullException.ThrowIfNull(sourceTrace);
        ArgumentNullException.ThrowIfNull(fileStore);
        ArgumentNullException.ThrowIfNull(stateMap);
        ArgumentNullException.ThrowIfNull(revisionAddresses);
        ArgumentNullException.ThrowIfNull(observations);

        SourceTrace = sourceTrace;
        Policy = policy;
        FileStore = fileStore;
        CurrentFileNumber = currentFileNumber;
        _stateMap = new(new Dictionary<uint, AbsoluteFrameAddress>(stateMap));
        _revisionAddresses = Array.AsReadOnly(revisionAddresses.ToArray());
        _observations = Array.AsReadOnly(observations.ToArray());
    }

    public WorkloadTrace SourceTrace { get; }

    public BaselinePolicy Policy { get; }

    public RbfFileStore FileStore { get; }

    public uint CurrentFileNumber { get; }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> StateMap => _stateMap;

    public IReadOnlyList<AbsoluteFrameAddress> RevisionAddresses => _revisionAddresses;

    public IReadOnlyList<RevisionObservation> Observations => _observations;
}
