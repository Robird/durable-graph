using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>A complete, immutable stored-exact DTO/string view of one Revision's live membership.</summary>
/// <remarks>
/// Objects may have different historical Schema versions. This view has no persisted roots,
/// domain instances, upgrades, or editable CaptureSession baseline. All content remains usable
/// after its stores close; the address identifies the queried view, not a published branch.
/// </remarks>
public sealed class DecodedRevision {
    private readonly Dictionary<ObjectId, ObjectStateRecord> _objects;

    // Synthetic exact-view fixtures have no persisted locators; product reads supply the actual map.
    internal DecodedRevision(FrameAddress revisionAddress, IEnumerable<ObjectStateRecord> objects, StringReadTable strings)
        : this(revisionAddress, objects, strings, new Dictionary<ObjectId, FrameAddress>()) { }

    internal DecodedRevision(FrameAddress revisionAddress, IEnumerable<ObjectStateRecord> objects, StringReadTable strings,
        IReadOnlyDictionary<ObjectId, FrameAddress> objectHeads) {
        RevisionAddress = revisionAddress;
        Objects = new FrozenList<ObjectStateRecord>(objects.OrderBy(static row => row.Id));
        _objects = Objects.ToDictionary(static row => row.Id);
        Strings = strings;
        ObjectHeads = new System.Collections.ObjectModel.ReadOnlyDictionary<ObjectId, FrameAddress>(
            objectHeads.ToDictionary(static pair => pair.Key, static pair => pair.Value));
    }

    public FrameAddress RevisionAddress { get; }
    public IReadOnlyList<ObjectStateRecord> Objects { get; }
    public StringReadTable Strings { get; }
    // Actual per-view locators, not a guess from the queried Revision's address.
    internal IReadOnlyDictionary<ObjectId, FrameAddress> ObjectHeads { get; }

    /// <summary>Returns the exact content for a live ID; zero and absent IDs are invalid.</summary>
    public ObjectStateRecord GetRequired(ObjectId objectId) => _objects.TryGetValue(objectId, out ObjectStateRecord? row)
        ? row
        : throw new InvalidDataException($"Object ID {objectId} is not live in this decoded Revision.");

    // Expose no ICollection.SyncRoot (Array.AsReadOnly would leak its backing array).
    private sealed class FrozenList<T>(IEnumerable<T> source) : IReadOnlyList<T> {
        private readonly T[] _items = source.ToArray();
        public int Count => _items.Length;
        public T this[int index] => _items[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
