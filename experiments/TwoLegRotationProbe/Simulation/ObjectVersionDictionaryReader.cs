using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal static class ObjectVersionDictionaryReader {
    public static ObjectVersionDictionaryMaterializationInspection MaterializeLive(
        RbfFileStore store,
        AbsoluteFrameAddress revisionAddress) {
        ArgumentNullException.ThrowIfNull(store);

        List<RevisionDictionaryRecord> revisionDictionaries = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        AbsoluteFrameAddress headRevisionAddress = revisionAddress;

        while (true) {
            if (!visited.Add(revisionAddress)) {
                throw new InvalidDataException(
                    $"Object-version dictionary materialization contains a Revision cycle at {revisionAddress}.");
            }

            Frame revision = ReadFrame(store, revisionAddress, objectId: null, "Revision");
            ObjectVersionDictionary dictionary = revision.ObjectVersionDictionary
                ?? throw new InvalidDataException(
                    $"Revision {revisionAddress} has no explicit object-version dictionary.");
            revisionDictionaries.Add(new(revisionAddress, revision, dictionary));

            switch (dictionary.Kind) {
                case ObjectVersionDictionaryKind.Base:
                    goto Replay;
                case ObjectVersionDictionaryKind.Delta:
                    RelativeFrameTicket parentTicket = dictionary.ParentRevisionFrameTicket
                        ?? throw new InvalidDataException(
                            $"Object-version dictionary Delta {revisionAddress} has no parent Revision.");
                    revisionAddress = ResolveEarlierAddress(
                        revisionAddress,
                        parentTicket,
                        objectId: null,
                        "Parent Revision");
                    break;
                default:
                    throw new InvalidDataException(
                        $"Revision {revisionAddress} has an invalid object-version dictionary kind.");
            }
        }

    Replay:
        SortedDictionary<uint, AbsoluteFrameAddress> liveBindings = [];
        for (int index = revisionDictionaries.Count - 1; index >= 0; index--) {
            RevisionDictionaryRecord source = revisionDictionaries[index];
            foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in
                source.Dictionary.Entries.OrderBy(entry => entry.Key)) {
                switch (binding.Kind) {
                    case ObjectVersionDictionaryBindingKind.Self:
                        if (!source.Revision.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"Revision {source.Address} binds object {objectId} to Self but has no matching object record.");
                        }

                        liveBindings[objectId] = source.Address;
                        break;
                    case ObjectVersionDictionaryBindingKind.External:
                        RelativeFrameTicket externalTicket = binding.ExternalFrameTicket
                            ?? throw new InvalidDataException(
                                $"Revision {source.Address} has an External binding without a frame ticket for object {objectId}.");
                        AbsoluteFrameAddress externalAddress = ResolveEarlierAddress(
                            source.Address,
                            externalTicket,
                            objectId,
                            "External binding");
                        if (externalAddress == source.Address) {
                            throw new InvalidDataException(
                                $"Revision {source.Address} has an External binding that aliases itself for object {objectId}.");
                        }

                        Frame externalFrame = ReadFrame(
                            store,
                            externalAddress,
                            objectId,
                            "External object-version frame");
                        if (!externalFrame.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"External frame {externalAddress} does not contain object {objectId}.");
                        }

                        liveBindings[objectId] = externalAddress;
                        break;
                    case ObjectVersionDictionaryBindingKind.Remove:
                        liveBindings.Remove(objectId);
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Revision {source.Address} has an invalid binding kind for object {objectId}.");
                }
            }
        }

        return new ObjectVersionDictionaryMaterializationInspection(
            headRevisionAddress,
            liveBindings,
            revisionDictionaries.Select(record => record.Address));
    }

    public static ObjectVersionDictionaryLookupInspection LookupLive(
        RbfFileStore store,
        AbsoluteFrameAddress revisionAddress,
        uint objectId) {
        ArgumentNullException.ThrowIfNull(store);

        List<AbsoluteFrameAddress> dictionaryRevisionAddresses = [];
        HashSet<AbsoluteFrameAddress> visited = [];

        while (true) {
            if (!visited.Add(revisionAddress)) {
                throw new InvalidDataException(
                    $"Object-version dictionary lookup for object {objectId} contains a Revision cycle at {revisionAddress}.");
            }

            Frame revision = ReadFrame(store, revisionAddress, objectId, "Revision");
            ObjectVersionDictionary dictionary = revision.ObjectVersionDictionary
                ?? throw new InvalidDataException(
                    $"Revision {revisionAddress} has no explicit object-version dictionary.");
            dictionaryRevisionAddresses.Add(revisionAddress);

            if (dictionary.Entries.TryGetValue(objectId, out ObjectVersionDictionaryBinding binding)) {
                switch (binding.Kind) {
                    case ObjectVersionDictionaryBindingKind.Self:
                        if (!revision.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"Revision {revisionAddress} binds object {objectId} to Self but has no matching object record.");
                        }

                        return Found(
                            objectId,
                            revisionAddress,
                            binding.Kind,
                            revisionAddress,
                            dictionaryRevisionAddresses);
                    case ObjectVersionDictionaryBindingKind.External:
                        RelativeFrameTicket externalTicket = binding.ExternalFrameTicket
                            ?? throw new InvalidDataException(
                                $"Revision {revisionAddress} has an External binding without a frame ticket for object {objectId}.");
                        AbsoluteFrameAddress externalAddress = ResolveEarlierAddress(
                            revisionAddress,
                            externalTicket,
                            objectId,
                            "External binding");
                        if (externalAddress == revisionAddress) {
                            throw new InvalidDataException(
                                $"Revision {revisionAddress} has an External binding that aliases itself for object {objectId}.");
                        }

                        Frame externalFrame = ReadFrame(
                            store,
                            externalAddress,
                            objectId,
                            "External object-version frame");
                        if (!externalFrame.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"External frame {externalAddress} does not contain object {objectId}.");
                        }

                        return Found(
                            objectId,
                            revisionAddress,
                            binding.Kind,
                            externalAddress,
                            dictionaryRevisionAddresses);
                    case ObjectVersionDictionaryBindingKind.Remove:
                        return new ObjectVersionDictionaryLookupInspection(
                            objectId,
                            ObjectVersionDictionaryLookupDisposition.Removed,
                            revisionAddress,
                            binding.Kind,
                            resolvedObjectVersionAddress: null,
                            dictionaryRevisionAddresses);
                    default:
                        throw new InvalidDataException(
                            $"Revision {revisionAddress} has an invalid binding kind for object {objectId}.");
                }
            }

            switch (dictionary.Kind) {
                case ObjectVersionDictionaryKind.Base:
                    return new ObjectVersionDictionaryLookupInspection(
                        objectId,
                        ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
                        revisionAddress,
                        bindingKind: null,
                        resolvedObjectVersionAddress: null,
                        dictionaryRevisionAddresses);
                case ObjectVersionDictionaryKind.Delta:
                    RelativeFrameTicket parentTicket = dictionary.ParentRevisionFrameTicket
                        ?? throw new InvalidDataException(
                            $"Object-version dictionary Delta {revisionAddress} has no parent Revision.");
                    revisionAddress = ResolveEarlierAddress(
                        revisionAddress,
                        parentTicket,
                        objectId,
                        "Parent Revision");
                    break;
                default:
                    throw new InvalidDataException(
                        $"Revision {revisionAddress} has an invalid object-version dictionary kind.");
            }
        }
    }

    private static ObjectVersionDictionaryLookupInspection Found(
        uint objectId,
        AbsoluteFrameAddress decisiveRevisionAddress,
        ObjectVersionDictionaryBindingKind bindingKind,
        AbsoluteFrameAddress resolvedObjectVersionAddress,
        IEnumerable<AbsoluteFrameAddress> dictionaryRevisionAddresses) => new(
            objectId,
            ObjectVersionDictionaryLookupDisposition.Found,
            decisiveRevisionAddress,
            bindingKind,
            resolvedObjectVersionAddress,
            dictionaryRevisionAddresses);

    private static AbsoluteFrameAddress ResolveEarlierAddress(
        AbsoluteFrameAddress containingRevisionAddress,
        RelativeFrameTicket relativeTicket,
        uint? objectId,
        string role) {
        AbsoluteFrameAddress resolvedAddress;
        try {
            resolvedAddress = new FileScope(containingRevisionAddress.FileNumber)
                .Resolve(relativeTicket);
        } catch (InvalidOperationException exception) {
            throw new InvalidDataException(
                $"{role} in Revision {containingRevisionAddress} has an invalid relative ticket{ObjectContext(objectId)}.",
                exception);
        }

        if (resolvedAddress.FileNumber == containingRevisionAddress.FileNumber &&
            resolvedAddress.FrameTicket.OffsetBytes >=
                containingRevisionAddress.FrameTicket.OffsetBytes) {
            throw new InvalidDataException(
                $"{role} in Revision {containingRevisionAddress} points to non-earlier frame {resolvedAddress}{ObjectContext(objectId)}.");
        }

        return resolvedAddress;
    }

    private static Frame ReadFrame(
        RbfFileStore store,
        AbsoluteFrameAddress address,
        uint? objectId,
        string role) {
        try {
            return store.ReadFrame(address);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"{role} {address} is missing{ObjectContext(objectId)}.",
                exception);
        }
    }

    private static string ObjectContext(uint? objectId) => objectId is { } value
        ? $" while looking up object {value}"
        : " while materializing the live object-version dictionary";

    private sealed record RevisionDictionaryRecord(
        AbsoluteFrameAddress Address,
        Frame Revision,
        ObjectVersionDictionary Dictionary);
}
