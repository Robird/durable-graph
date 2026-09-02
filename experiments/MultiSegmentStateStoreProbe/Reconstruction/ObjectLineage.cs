using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

/// <summary>
/// Diagnostic historical lineage ordered from the requested ObjectVersion head toward
/// its root. This is not a second current-state authority.
/// </summary>
internal sealed class ObjectLineage {
    private readonly ReadOnlyCollection<ObjectLineageEntry> _entries;

    internal ObjectLineage(uint objectId, IEnumerable<ObjectLineageEntry> entries) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
        ArgumentNullException.ThrowIfNull(entries);

        ObjectId = objectId;
        _entries = Array.AsReadOnly(entries.ToArray());
        if (_entries.Count == 0) {
            throw new ArgumentException(
                "An ObjectVersion lineage must contain its requested head.",
                nameof(entries));
        }
    }

    public uint ObjectId { get; }

    public IReadOnlyList<ObjectLineageEntry> Entries => _entries;
}

internal readonly record struct ObjectLineageEntry(
    AbsoluteFrameAddress Address,
    ObjectVersionKind Kind,
    LogicalObjectState State);
