using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class ObjectVersionDictionaryMaterializationInspection {
    internal ObjectVersionDictionaryMaterializationInspection(
        AbsoluteFrameAddress headRevisionAddress,
        IEnumerable<KeyValuePair<uint, AbsoluteFrameAddress>> bindings,
        IEnumerable<AbsoluteFrameAddress> dictionaryRevisionAddresses) {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(dictionaryRevisionAddresses);

        SortedDictionary<uint, AbsoluteFrameAddress> frozenBindings = [];
        foreach ((uint objectId, AbsoluteFrameAddress address) in bindings) {
            if (!frozenBindings.TryAdd(objectId, address)) {
                throw new ArgumentException(
                    $"Materialized object-version dictionary contains duplicate ObjectId {objectId}.",
                    nameof(bindings));
            }
        }

        AbsoluteFrameAddress[] frozenRevisionAddresses =
            dictionaryRevisionAddresses.ToArray();
        if (frozenRevisionAddresses.Length == 0 ||
            frozenRevisionAddresses[0] != headRevisionAddress) {
            throw new ArgumentException(
                "A materialization trace must begin with its head Revision.",
                nameof(dictionaryRevisionAddresses));
        }

        HeadRevisionAddress = headRevisionAddress;
        Bindings = new ReadOnlyDictionary<uint, AbsoluteFrameAddress>(frozenBindings);
        DictionaryRevisionAddresses = new ReadOnlyCollection<AbsoluteFrameAddress>(
            frozenRevisionAddresses);
    }

    public AbsoluteFrameAddress HeadRevisionAddress { get; }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> Bindings { get; }

    public IReadOnlyList<AbsoluteFrameAddress> DictionaryRevisionAddresses { get; }
}
