using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed class ObjectVersionDictionaryLookupInspection {
    internal ObjectVersionDictionaryLookupInspection(
        uint objectId,
        ObjectVersionDictionaryLookupDisposition disposition,
        AbsoluteFrameAddress decisiveRevisionAddress,
        ObjectVersionDictionaryBindingKind? bindingKind,
        AbsoluteFrameAddress? resolvedObjectVersionAddress,
        IEnumerable<AbsoluteFrameAddress> dictionaryRevisionAddresses) {
        ArgumentNullException.ThrowIfNull(dictionaryRevisionAddresses);

        if (!Enum.IsDefined(disposition)) {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        if (bindingKind is { } definedBindingKind && !Enum.IsDefined(definedBindingKind)) {
            throw new ArgumentOutOfRangeException(nameof(bindingKind));
        }

        AbsoluteFrameAddress[] frozenRevisionAddresses =
            dictionaryRevisionAddresses.ToArray();
        if (frozenRevisionAddresses.Length == 0) {
            throw new ArgumentException(
                "An object-version dictionary lookup inspection must contain at least one Revision address.",
                nameof(dictionaryRevisionAddresses));
        }

        if (frozenRevisionAddresses[^1] != decisiveRevisionAddress) {
            throw new ArgumentException(
                "The decisive Revision must be the final inspected dictionary Revision.",
                nameof(decisiveRevisionAddress));
        }

        ValidateOutcome(
            disposition,
            decisiveRevisionAddress,
            bindingKind,
            resolvedObjectVersionAddress);

        ObjectId = objectId;
        Disposition = disposition;
        DecisiveRevisionAddress = decisiveRevisionAddress;
        BindingKind = bindingKind;
        ResolvedObjectVersionAddress = resolvedObjectVersionAddress;
        DictionaryRevisionAddresses = new ReadOnlyCollection<AbsoluteFrameAddress>(
            frozenRevisionAddresses);
    }

    public uint ObjectId { get; }

    public ObjectVersionDictionaryLookupDisposition Disposition { get; }

    public AbsoluteFrameAddress DecisiveRevisionAddress { get; }

    public ObjectVersionDictionaryBindingKind? BindingKind { get; }

    public AbsoluteFrameAddress? ResolvedObjectVersionAddress { get; }

    public IReadOnlyList<AbsoluteFrameAddress> DictionaryRevisionAddresses { get; }

    private static void ValidateOutcome(
        ObjectVersionDictionaryLookupDisposition disposition,
        AbsoluteFrameAddress decisiveRevisionAddress,
        ObjectVersionDictionaryBindingKind? bindingKind,
        AbsoluteFrameAddress? resolvedObjectVersionAddress) {
        switch (disposition) {
            case ObjectVersionDictionaryLookupDisposition.Found:
                if (bindingKind is not (
                    ObjectVersionDictionaryBindingKind.Self or
                    ObjectVersionDictionaryBindingKind.External) ||
                    resolvedObjectVersionAddress is null) {
                    throw new ArgumentException(
                        "A Found lookup requires a Self or External binding and a resolved object-version address.");
                }

                if (bindingKind == ObjectVersionDictionaryBindingKind.Self &&
                    resolvedObjectVersionAddress != decisiveRevisionAddress) {
                    throw new ArgumentException(
                        "A Self binding must resolve to its decisive Revision.");
                }

                if (bindingKind == ObjectVersionDictionaryBindingKind.External &&
                    resolvedObjectVersionAddress == decisiveRevisionAddress) {
                    throw new ArgumentException(
                        "An External binding cannot resolve to its decisive Revision.");
                }

                break;
            case ObjectVersionDictionaryLookupDisposition.Removed:
                if (bindingKind != ObjectVersionDictionaryBindingKind.Remove ||
                    resolvedObjectVersionAddress is not null) {
                    throw new ArgumentException(
                        "A Removed lookup requires a Remove binding and no resolved object-version address.");
                }

                break;
            case ObjectVersionDictionaryLookupDisposition.AbsentAtBase:
                if (bindingKind is not null || resolvedObjectVersionAddress is not null) {
                    throw new ArgumentException(
                        "An AbsentAtBase lookup cannot carry a binding or resolved object-version address.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition));
        }
    }
}
