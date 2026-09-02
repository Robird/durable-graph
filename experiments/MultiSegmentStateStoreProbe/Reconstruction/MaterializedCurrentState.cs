using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

internal sealed class MaterializedCurrentState {
    private readonly ReadOnlyDictionary<uint, AbsoluteFrameAddress> _objectVersionHeads;
    private readonly ReadOnlyDictionary<uint, LogicalObjectState> _states;
    private readonly ReadOnlyDictionary<uint, IReadOnlyList<AbsoluteFrameAddress>>
        _objectReconstructionPaths;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _ovdRevisionAddresses;

    internal MaterializedCurrentState(
        AbsoluteFrameAddress publishedHead,
        IEnumerable<KeyValuePair<uint, AbsoluteFrameAddress>> objectVersionHeads,
        IEnumerable<KeyValuePair<uint, LogicalObjectState>> states,
        IEnumerable<KeyValuePair<uint, IReadOnlyList<AbsoluteFrameAddress>>>
            objectReconstructionPaths,
        IEnumerable<AbsoluteFrameAddress> ovdRevisionAddresses) {
        PublishedHead = publishedHead;
        _objectVersionHeads = new(new SortedDictionary<uint, AbsoluteFrameAddress>(
            objectVersionHeads.ToDictionary()));
        _states = new(new SortedDictionary<uint, LogicalObjectState>(
            states.ToDictionary()));

        SortedDictionary<uint, IReadOnlyList<AbsoluteFrameAddress>> frozenPaths = [];
        foreach ((uint objectId, IReadOnlyList<AbsoluteFrameAddress> path) in
            objectReconstructionPaths) {
            frozenPaths.Add(objectId, Array.AsReadOnly(path.ToArray()));
        }

        _objectReconstructionPaths = new(frozenPaths);
        _ovdRevisionAddresses = Array.AsReadOnly(ovdRevisionAddresses.ToArray());
        if (!_objectVersionHeads.Keys.SequenceEqual(_states.Keys) ||
            !_states.Keys.SequenceEqual(_objectReconstructionPaths.Keys)) {
            throw new ArgumentException(
                "Materialized current-state collections must cover the same ObjectIds.");
        }
    }

    public AbsoluteFrameAddress PublishedHead { get; }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> ObjectVersionHeads =>
        _objectVersionHeads;

    public IReadOnlyDictionary<uint, LogicalObjectState> States => _states;

    public IReadOnlyDictionary<uint, IReadOnlyList<AbsoluteFrameAddress>>
        ObjectReconstructionPaths => _objectReconstructionPaths;

    public IReadOnlyList<AbsoluteFrameAddress> OvdRevisionAddresses =>
        _ovdRevisionAddresses;
}
