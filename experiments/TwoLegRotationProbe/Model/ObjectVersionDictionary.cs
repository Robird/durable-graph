using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class ObjectVersionDictionary {
    private readonly ReadOnlyDictionary<uint, ObjectVersionDictionaryBinding> _entries;

    internal ObjectVersionDictionary(
        ObjectVersionDictionaryKind kind,
        RelativeFrameTicket? parentRevisionFrameTicket,
        IEnumerable<KeyValuePair<uint, ObjectVersionDictionaryBinding>> entries) {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == ObjectVersionDictionaryKind.Delta && parentRevisionFrameTicket is null) {
            throw new ArgumentException(
                "An object-version dictionary Delta must have a parent Revision.",
                nameof(parentRevisionFrameTicket));
        }

        Dictionary<uint, ObjectVersionDictionaryBinding> frozenEntries = [];
        foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in entries) {
            ValidateBinding(kind, objectId, binding);
            if (!frozenEntries.TryAdd(objectId, binding)) {
                throw new ArgumentException(
                    $"Object-version dictionary contains duplicate ObjectId {objectId}.",
                    nameof(entries));
            }
        }

        Kind = kind;
        ParentRevisionFrameTicket = parentRevisionFrameTicket;
        _entries = new(frozenEntries);
    }

    public ObjectVersionDictionaryKind Kind { get; }

    /// <summary>
    /// The containing Revision's prior snapshot. OVD Delta also uses it as its replay parent;
    /// OVD Base does not inherit live bindings from it, but Base lineage still uses it.
    /// </summary>
    public RelativeFrameTicket? ParentRevisionFrameTicket { get; }

    public IReadOnlyDictionary<uint, ObjectVersionDictionaryBinding> Entries => _entries;

    private static void ValidateBinding(
        ObjectVersionDictionaryKind dictionaryKind,
        uint objectId,
        ObjectVersionDictionaryBinding binding) {
        if (!Enum.IsDefined(binding.Kind)) {
            throw new ArgumentOutOfRangeException(
                nameof(binding),
                binding.Kind,
                $"Object-version dictionary entry {objectId} has an invalid binding kind.");
        }

        switch (binding.Kind) {
            case ObjectVersionDictionaryBindingKind.Self:
                if (binding.ExternalFrameTicket is not null) {
                    throw new ArgumentException(
                        $"Self binding for ObjectId {objectId} cannot carry an external frame ticket.",
                        nameof(binding));
                }

                break;
            case ObjectVersionDictionaryBindingKind.External:
                if (binding.ExternalFrameTicket is null) {
                    throw new ArgumentException(
                        $"External binding for ObjectId {objectId} must carry a frame ticket.",
                        nameof(binding));
                }

                break;
            case ObjectVersionDictionaryBindingKind.Remove:
                if (binding.ExternalFrameTicket is not null) {
                    throw new ArgumentException(
                        $"Remove binding for ObjectId {objectId} cannot carry an external frame ticket.",
                        nameof(binding));
                }

                if (dictionaryKind == ObjectVersionDictionaryKind.Base) {
                    throw new ArgumentException(
                        $"Object-version dictionary Base cannot remove ObjectId {objectId}.",
                        nameof(binding));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binding));
        }
    }
}
