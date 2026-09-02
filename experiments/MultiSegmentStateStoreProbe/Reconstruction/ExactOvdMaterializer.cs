using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Reconstruction;

/// <summary>
/// Sole exact-head OVD replay primitive shared by current loading and historical
/// point lookup. It interprets membership only; it never reconstructs object payloads.
/// </summary>
internal static class ExactOvdMaterializer {
    public static ExactOvdMaterialization Materialize(
        InMemorySegmentStore store,
        AbsoluteFrameAddress publishedHead,
        RenderedFrameCandidate? overlay = null) {
        ArgumentNullException.ThrowIfNull(store);

        List<RevisionRecord> records = [];
        HashSet<AbsoluteFrameAddress> visited = [];
        HashSet<AbsoluteFrameAddress> requiredFrames = [];
        AbsoluteFrameAddress address = publishedHead;

        while (true) {
            if (!visited.Add(address)) {
                throw new InvalidDataException(
                    $"OVD reconstruction contains a Revision cycle at {address}.");
            }

            RevisionFrame revision = ReadRevision(store, address, "Revision", overlay);
            _ = requiredFrames.Add(address);
            AbsoluteFrameAddress? prior = ResolveOptionalPrior(address, revision);
            records.Add(new RevisionRecord(address, revision));
            switch (revision.ObjectVersionDictionary.Kind) {
                case ObjectVersionDictionaryKind.Base:
                    goto Replay;
                case ObjectVersionDictionaryKind.Delta:
                    address = prior ?? throw new InvalidDataException(
                        $"OVD Delta Revision {address} has no shared PriorRevision.");
                    break;
                default:
                    throw new InvalidDataException(
                        $"Revision {address} has an invalid OVD kind.");
            }
        }

    Replay:
        SortedDictionary<uint, ExactOvdLookup> lookups = [];
        for (int index = records.Count - 1; index >= 0; index--) {
            RevisionRecord source = records[index];
            foreach ((uint objectId, ObjectVersionDictionaryBinding binding) in
                source.Revision.ObjectVersionDictionary.Entries) {
                switch (binding.Kind) {
                    case ObjectVersionDictionaryBindingKind.BindSelf:
                        if (!source.Revision.ObjectVersions.ContainsKey(objectId)) {
                            throw new InvalidDataException(
                                $"Revision {source.Address} binds ObjectId {objectId} to Self " +
                                "without a same-ObjectId record.");
                        }

                        lookups[objectId] = ExactOvdLookup.Found(source.Address);
                        break;
                    case ObjectVersionDictionaryBindingKind.External:
                        RelativeFrameTicket external = binding.ExternalReference
                            ?? throw new InvalidDataException(
                                $"External binding for ObjectId {objectId} has no reference.");
                        AbsoluteFrameAddress externalAddress = ResolveEarlier(
                            source.Address,
                            external,
                            $"External binding for ObjectId {objectId}");
                        lookups[objectId] = ExactOvdLookup.Found(externalAddress);
                        break;
                    case ObjectVersionDictionaryBindingKind.Remove:
                        lookups[objectId] = ExactOvdLookup.Removed();
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Revision {source.Address} has an invalid OVD binding kind.");
                }
            }
        }

        foreach ((uint objectId, ExactOvdLookup lookup) in lookups) {
            if (lookup.Kind != ExactOvdLookupKind.Found) {
                continue;
            }

            AbsoluteFrameAddress objectVersionHead = lookup.ObjectVersionHead
                ?? throw new InvalidDataException(
                    $"Found OVD binding for ObjectId {objectId} has no head.");
            RevisionFrame target = ReadRevision(
                store,
                objectVersionHead,
                $"Live ObjectVersion for ObjectId {objectId}",
                overlay);
            _ = requiredFrames.Add(objectVersionHead);
            if (!target.ObjectVersions.ContainsKey(objectId)) {
                throw new InvalidDataException(
                    $"Live ObjectVersion Frame {objectVersionHead} does not contain " +
                    $"ObjectId {objectId}.");
            }
        }

        return new ExactOvdMaterialization(
            lookups,
            records.Select(static record => record.Address),
            requiredFrames);
    }

    private static AbsoluteFrameAddress? ResolveOptionalPrior(
        AbsoluteFrameAddress containingAddress,
        RevisionFrame revision) => revision.PriorRevision is { } prior
            ? ResolveEarlier(containingAddress, prior, "PriorRevision")
            : null;

    private static AbsoluteFrameAddress ResolveEarlier(
        AbsoluteFrameAddress containingAddress,
        RelativeFrameTicket reference,
        string role) {
        try {
            return new FileScope(containingAddress.FileNumber).ResolveEarlier(
                containingAddress.FrameTicket,
                reference);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or InvalidDataException) {
            throw new InvalidDataException(
                $"{role} in Frame {containingAddress} is not a valid earlier reference.",
                exception);
        }
    }

    private static RevisionFrame ReadRevision(
        InMemorySegmentStore store,
        AbsoluteFrameAddress address,
        string role,
        RenderedFrameCandidate? overlay) {
        if (overlay is not null && address == overlay.Address) {
            return overlay.RevisionFrame ?? throw new InvalidDataException(
                $"{role} overlay Frame {address} is not a semantic Revision Frame.");
        }

        RenderedFrameCandidate candidate;
        try {
            candidate = store.Read(address);
        } catch (KeyNotFoundException exception) {
            throw new InvalidDataException($"{role} Frame {address} is missing.", exception);
        }

        return candidate.RevisionFrame ?? throw new InvalidDataException(
            $"{role} Frame {address} is not a semantic Revision Frame.");
    }

    private sealed record RevisionRecord(
        AbsoluteFrameAddress Address,
        RevisionFrame Revision);
}

internal sealed class ExactOvdMaterialization {
    private readonly ReadOnlyDictionary<uint, ExactOvdLookup> _lookups;
    private readonly ReadOnlyDictionary<uint, AbsoluteFrameAddress> _bindings;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _revisionAddresses;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _requiredFrameAddresses;

    internal ExactOvdMaterialization(
        IEnumerable<KeyValuePair<uint, ExactOvdLookup>> lookups,
        IEnumerable<AbsoluteFrameAddress> revisionAddresses,
        IEnumerable<AbsoluteFrameAddress> requiredFrameAddresses) {
        SortedDictionary<uint, ExactOvdLookup> frozenLookups = new(
            lookups.ToDictionary());
        _lookups = new(frozenLookups);
        _bindings = new(new SortedDictionary<uint, AbsoluteFrameAddress>(
            frozenLookups
                .Where(static pair => pair.Value.Kind == ExactOvdLookupKind.Found)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ObjectVersionHead!.Value)));
        _revisionAddresses = Array.AsReadOnly(revisionAddresses.ToArray());
        _requiredFrameAddresses = Array.AsReadOnly(requiredFrameAddresses
            .Distinct()
            .OrderBy(static address => address.FileNumber.Value)
            .ThenBy(static address => address.FrameTicket.OffsetBytes)
            .ThenBy(static address => address.FrameTicket.LengthBytes)
            .ToArray());
    }

    public IReadOnlyDictionary<uint, AbsoluteFrameAddress> Bindings => _bindings;

    public IReadOnlyList<AbsoluteFrameAddress> RevisionAddresses => _revisionAddresses;

    /// <summary>Every Frame actually read while materializing and validating this OVD.</summary>
    public IReadOnlyList<AbsoluteFrameAddress> RequiredFrameAddresses =>
        _requiredFrameAddresses;

    public ExactOvdLookup Lookup(uint objectId) =>
        _lookups.TryGetValue(objectId, out ExactOvdLookup lookup)
            ? lookup
            : ExactOvdLookup.AbsentAtBase();
}

internal enum ExactOvdLookupKind {
    Found,
    AbsentAtBase,
    Removed,
}

internal readonly record struct ExactOvdLookup(
    ExactOvdLookupKind Kind,
    AbsoluteFrameAddress? ObjectVersionHead) {
    public static ExactOvdLookup Found(AbsoluteFrameAddress objectVersionHead) =>
        new(ExactOvdLookupKind.Found, objectVersionHead);

    public static ExactOvdLookup AbsentAtBase() =>
        new(ExactOvdLookupKind.AbsentAtBase, null);

    public static ExactOvdLookup Removed() =>
        new(ExactOvdLookupKind.Removed, null);
}
