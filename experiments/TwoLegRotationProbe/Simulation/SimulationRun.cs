using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class SimulationRun {
    private readonly ReadOnlyDictionary<uint, AbsoluteFrameAddress> _stateMap;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _revisionAddresses;
    private readonly ReadOnlyCollection<RevisionObservation> _observations;
    private readonly ReadOnlyDictionary<AbsoluteFrameAddress, FrameAccountingEstimate>
        _accountingEstimates;

    internal SimulationRun(
        WorkloadTrace sourceTrace,
        BaselinePolicy policy,
        AccountingScope accountingScope,
        RbfFileStore fileStore,
        uint currentFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap,
        IEnumerable<AbsoluteFrameAddress> revisionAddresses,
        IEnumerable<RevisionObservation> observations,
        IReadOnlyDictionary<AbsoluteFrameAddress, FrameAccountingEstimate>
            accountingEstimates) {
        ArgumentNullException.ThrowIfNull(sourceTrace);
        ArgumentNullException.ThrowIfNull(fileStore);
        ArgumentNullException.ThrowIfNull(stateMap);
        ArgumentNullException.ThrowIfNull(revisionAddresses);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(accountingEstimates);
        if (!Enum.IsDefined(accountingScope)) {
            throw new ArgumentOutOfRangeException(nameof(accountingScope));
        }

        SourceTrace = sourceTrace;
        Policy = policy;
        AccountingScope = accountingScope;
        FileStore = fileStore;
        CurrentFileNumber = currentFileNumber;
        _stateMap = new(new Dictionary<uint, AbsoluteFrameAddress>(stateMap));
        _revisionAddresses = Array.AsReadOnly(revisionAddresses.ToArray());
        _observations = Array.AsReadOnly(observations.ToArray());
        _accountingEstimates = new(new Dictionary<AbsoluteFrameAddress, FrameAccountingEstimate>(
            accountingEstimates));

        if (_revisionAddresses.Count != _accountingEstimates.Count ||
            _revisionAddresses.Any(address => !_accountingEstimates.ContainsKey(address)) ||
            _accountingEstimates.Values.Any(estimate => estimate.Scope != accountingScope) ||
            _observations.Any(observation => observation.AccountingScope != accountingScope)) {
            throw new ArgumentException(
                "A SimulationRun must contain one complete accounting scope.",
                nameof(accountingEstimates));
        }
    }

    public WorkloadTrace SourceTrace { get; }

    public BaselinePolicy Policy { get; }

    public AccountingScope AccountingScope { get; }

    public RbfFileStore FileStore { get; }

    public uint CurrentFileNumber { get; }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> StateMap => _stateMap;

    public IReadOnlyList<AbsoluteFrameAddress> RevisionAddresses => _revisionAddresses;

    public IReadOnlyList<RevisionObservation> Observations => _observations;

    public IReadOnlyDictionary<AbsoluteFrameAddress, FrameAccountingEstimate>
        AccountingEstimates => _accountingEstimates;
}
