using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal enum UpdateWriteMode : byte {
    Base = 1,
    Delta = 2,
}

internal enum NoChangeWriteMode : byte {
    Inherit = 1,
    SameStateRebase = 2,
}

internal readonly record struct UpdateWriteSelection(uint ObjectId, UpdateWriteMode Mode);

internal readonly record struct NoChangeWriteSelection(
    uint ObjectId,
    NoChangeWriteMode Mode);

internal sealed class RevisionSaveSelection {
    private readonly ReadOnlyDictionary<uint, UpdateWriteMode> _updates;
    private readonly ReadOnlyDictionary<uint, NoChangeWriteMode> _noChanges;

    public RevisionSaveSelection(
        ObjectVersionDictionaryKind dictionaryKind,
        IEnumerable<UpdateWriteSelection> updates,
        IEnumerable<NoChangeWriteSelection> noChanges) {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(noChanges);
        if (!Enum.IsDefined(dictionaryKind)) {
            throw new ArgumentOutOfRangeException(nameof(dictionaryKind));
        }

        DictionaryKind = dictionaryKind;
        _updates = new(FreezeUpdates(updates));
        _noChanges = new(FreezeNoChanges(noChanges));
    }

    public ObjectVersionDictionaryKind DictionaryKind { get; }

    public IReadOnlyDictionary<uint, UpdateWriteMode> Updates => _updates;

    public IReadOnlyDictionary<uint, NoChangeWriteMode> NoChanges => _noChanges;

    private static SortedDictionary<uint, UpdateWriteMode> FreezeUpdates(
        IEnumerable<UpdateWriteSelection> source) {
        SortedDictionary<uint, UpdateWriteMode> result = [];
        foreach (UpdateWriteSelection selection in source) {
            ArgumentOutOfRangeException.ThrowIfZero(selection.ObjectId);
            if (!Enum.IsDefined(selection.Mode)) {
                throw new ArgumentOutOfRangeException(nameof(source));
            }

            if (!result.TryAdd(selection.ObjectId, selection.Mode)) {
                throw new ArgumentException(
                    $"Update ObjectId {selection.ObjectId} is selected more than once.",
                    nameof(source));
            }
        }

        return result;
    }

    private static SortedDictionary<uint, NoChangeWriteMode> FreezeNoChanges(
        IEnumerable<NoChangeWriteSelection> source) {
        SortedDictionary<uint, NoChangeWriteMode> result = [];
        foreach (NoChangeWriteSelection selection in source) {
            ArgumentOutOfRangeException.ThrowIfZero(selection.ObjectId);
            if (!Enum.IsDefined(selection.Mode)) {
                throw new ArgumentOutOfRangeException(nameof(source));
            }

            if (!result.TryAdd(selection.ObjectId, selection.Mode)) {
                throw new ArgumentException(
                    $"NoChange ObjectId {selection.ObjectId} is selected more than once.",
                    nameof(source));
            }
        }

        return result;
    }
}
