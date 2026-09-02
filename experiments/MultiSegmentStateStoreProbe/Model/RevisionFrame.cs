using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Model;

/// <summary>
/// Immutable semantic content of one probe Revision Frame. The shared prior anchor belongs
/// to the Revision, not to its OVD.
/// </summary>
internal sealed class RevisionFrame {
    private readonly ReadOnlyDictionary<uint, ObjectVersion> _objectVersions;

    internal RevisionFrame(
        RelativeFrameTicket? priorRevision,
        ObjectVersionDictionary objectVersionDictionary,
        IEnumerable<ObjectVersion> objectVersions) {
        ArgumentNullException.ThrowIfNull(objectVersionDictionary);
        ArgumentNullException.ThrowIfNull(objectVersions);

        SortedDictionary<uint, ObjectVersion> frozenVersions = [];
        foreach (ObjectVersion version in objectVersions) {
            ArgumentNullException.ThrowIfNull(version);
            if (!frozenVersions.TryAdd(version.ObjectId, version)) {
                throw new ArgumentException(
                    $"Revision contains duplicate ObjectId {version.ObjectId} records.",
                    nameof(objectVersions));
            }
        }

        foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in
            objectVersionDictionary.Entries) {
            if (binding.Kind == ObjectVersionDictionaryBindingKind.BindSelf &&
                !frozenVersions.ContainsKey(objectId)) {
                throw new ArgumentException(
                    $"BindSelf for ObjectId {objectId} requires a same-ObjectId record.",
                    nameof(objectVersionDictionary));
            }
        }

        foreach (uint objectId in frozenVersions.Keys) {
            if (!objectVersionDictionary.Entries.TryGetValue(
                objectId,
                out ObjectVersionDictionaryBinding binding) ||
                binding.Kind != ObjectVersionDictionaryBindingKind.BindSelf) {
                throw new ArgumentException(
                    $"ObjectVersion {objectId} requires a canonical BindSelf entry.",
                    nameof(objectVersions));
            }
        }

        PriorRevision = priorRevision;
        ObjectVersionDictionary = objectVersionDictionary;
        _objectVersions = new(frozenVersions);
    }

    public RelativeFrameTicket? PriorRevision { get; }

    public ObjectVersionDictionary ObjectVersionDictionary { get; }

    public IReadOnlyDictionary<uint, ObjectVersion> ObjectVersions => _objectVersions;
}
