using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class Frame {
    private readonly ReadOnlyDictionary<uint, ObjectVersion> _objectVersions;

    internal Frame(
        IReadOnlyDictionary<uint, ObjectVersion> objectVersions,
        ObjectVersionDictionary? objectVersionDictionary = null) {
        ArgumentNullException.ThrowIfNull(objectVersions);
        _objectVersions = new(new Dictionary<uint, ObjectVersion>(objectVersions));

        if (objectVersionDictionary is not null) {
            foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in
                objectVersionDictionary.Entries) {
                if (binding.Kind == ObjectVersionDictionaryBindingKind.Self &&
                    !_objectVersions.ContainsKey(objectId)) {
                    throw new ArgumentException(
                        $"Self binding for ObjectId {objectId} requires a domain record in the same Frame.",
                        nameof(objectVersionDictionary));
                }
            }
        }

        ObjectVersionDictionary = objectVersionDictionary;
    }

    public IReadOnlyDictionary<uint, ObjectVersion> ObjectVersions => _objectVersions;

    public ObjectVersionDictionary? ObjectVersionDictionary { get; }
}
