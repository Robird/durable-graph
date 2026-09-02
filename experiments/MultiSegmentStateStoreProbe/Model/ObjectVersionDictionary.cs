using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal sealed class ObjectVersionDictionary {
    private readonly ReadOnlyDictionary<uint, ObjectVersionDictionaryBinding> _entries;

    internal ObjectVersionDictionary(
        ObjectVersionDictionaryKind kind,
        IEnumerable<KeyValuePair<uint, ObjectVersionDictionaryBinding>> entries) {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        SortedDictionary<uint, ObjectVersionDictionaryBinding> frozen = [];
        foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in entries) {
            ArgumentOutOfRangeException.ThrowIfZero(objectId);
            ValidateBinding(kind, objectId, binding);
            if (!frozen.TryAdd(objectId, binding)) {
                throw new ArgumentException(
                    $"ObjectVersionDictionary contains duplicate ObjectId {objectId}.",
                    nameof(entries));
            }
        }

        Kind = kind;
        _entries = new(frozen);
    }

    public ObjectVersionDictionaryKind Kind { get; }

    public IReadOnlyDictionary<uint, ObjectVersionDictionaryBinding> Entries => _entries;

    private static void ValidateBinding(
        ObjectVersionDictionaryKind dictionaryKind,
        uint objectId,
        ObjectVersionDictionaryBinding binding) {
        if (!Enum.IsDefined(binding.Kind)) {
            throw new ArgumentOutOfRangeException(
                nameof(binding),
                binding.Kind,
                $"ObjectVersionDictionary entry {objectId} has an invalid binding kind.");
        }

        switch (binding.Kind) {
            case ObjectVersionDictionaryBindingKind.BindSelf:
                if (binding.ExternalReference is not null) {
                    throw new ArgumentException(
                        $"BindSelf for ObjectId {objectId} cannot carry an external reference.",
                        nameof(binding));
                }

                break;
            case ObjectVersionDictionaryBindingKind.External:
                if (dictionaryKind != ObjectVersionDictionaryKind.Base) {
                    throw new ArgumentException(
                        "External bindings are canonical only in an OVD Base.",
                        nameof(binding));
                }

                if (binding.ExternalReference is null) {
                    throw new ArgumentException(
                        $"External binding for ObjectId {objectId} requires a reference.",
                        nameof(binding));
                }

                break;
            case ObjectVersionDictionaryBindingKind.Remove:
                if (dictionaryKind != ObjectVersionDictionaryKind.Delta) {
                    throw new ArgumentException(
                        "Remove bindings are canonical only in an OVD Delta.",
                        nameof(binding));
                }

                if (binding.ExternalReference is not null) {
                    throw new ArgumentException(
                        $"Remove for ObjectId {objectId} cannot carry a reference.",
                        nameof(binding));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binding));
        }
    }
}
