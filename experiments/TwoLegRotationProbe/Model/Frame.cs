using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class Frame {
    private readonly ReadOnlyDictionary<uint, ObjectVersion> _objectVersions;

    internal Frame(IReadOnlyDictionary<uint, ObjectVersion> objectVersions) {
        ArgumentNullException.ThrowIfNull(objectVersions);
        _objectVersions = new(new Dictionary<uint, ObjectVersion>(objectVersions));
    }

    public IReadOnlyDictionary<uint, ObjectVersion> ObjectVersions => _objectVersions;
}
