using System.Collections;
using Atelia.Data;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionTests {
    private static readonly FrameAddress Parent = Address(1, 32);

    [Fact]
    public void Base_freezes_canonical_records_and_external_collections() {
        BaseObjectRecord nine = new(9, [90]);
        BaseObjectRecord one = new(1, [10]);
        List<BaseObjectRecord> records = [nine, one];
        Dictionary<uint, FrameAddress> external = new() {
            [8] = Address(1, 64),
            [2] = Address(1, 96),
        };

        StateRevision revision = StateRevision.CreateBase(Parent, records, external);
        records.Clear();
        external.Clear();

        Assert.Equal(ObjectHeadMapKind.Base, revision.ObjectHeadMapKind);
        Assert.Equal([1u, 9u], revision.BaseObjectIds);
        Assert.Equal([one, nine], revision.BaseObjects);
        Assert.Same(one, revision.BaseObjects[0]);
        Assert.Equal([2u, 8u], revision.ExternalObjectHeads.Keys);
        Assert.Equal([Address(1, 96), Address(1, 64)], revision.ExternalObjectHeads.Values);
        Assert.Empty(revision.RemovedObjectIds);
    }

    [Fact]
    public void Delta_freezes_canonical_removed_ids_and_requires_parent() {
        List<uint> removed = [9, 2];
        StateRevision revision = StateRevision.CreateDelta(Parent, [new(3, [30]), new(7, [])], removed);
        removed.Clear();

        Assert.Equal(ObjectHeadMapKind.Delta, revision.ObjectHeadMapKind);
        Assert.Equal(Parent, revision.ParentRevisionAddress);
        Assert.Equal([3u, 7u], revision.BaseObjectIds);
        Assert.Equal([2u, 9u], revision.RemovedObjectIds);
        Assert.Empty(revision.ExternalObjectHeads);
        Assert.Throws<ArgumentOutOfRangeException>(() => StateRevision.CreateDelta(default, [], []));
    }

    [Fact]
    public void Collections_do_not_expose_mutable_interfaces_or_SyncRoot_backing() {
        StateRevision revision = StateRevision.CreateBase(Parent, [new(3, [30])], [new(4, Parent)]);
        StateRevision delta = StateRevision.CreateDelta(Parent, [], [3]);
        object[] collections = [
            revision.BaseObjects, revision.BaseObjectIds, revision.ExternalObjectHeads,
            revision.ExternalObjectHeads.Keys, revision.ExternalObjectHeads.Values,
            delta.RemovedObjectIds,
        ];
        foreach (object collection in collections) {
            Assert.False(collection is ICollection);
            Assert.False(collection is IList);
            Assert.False(collection is IDictionary);
        }

        Assert.False(revision.BaseObjects is ICollection<BaseObjectRecord>);
        Assert.False(revision.BaseObjectIds is ICollection<uint>);
        Assert.False(revision.ExternalObjectHeads is IDictionary<uint, FrameAddress>);
        Assert.False(delta.RemovedObjectIds is ICollection<uint>);
        BaseObjectRecord[] returnedCopy = revision.BaseObjects.ToArray();
        returnedCopy[0] = new(99, [99]);
        Assert.Equal(3u, revision.BaseObjects[0].ObjectId);
        Assert.Equal([3u], revision.BaseObjectIds);
    }

    [Fact]
    public void Null_collections_and_null_records_are_rejected() {
        Assert.Throws<ArgumentNullException>(() => StateRevision.CreateBase(null, null!, []));
        Assert.Throws<ArgumentNullException>(() => StateRevision.CreateBase(null, [], null!));
        Assert.Throws<ArgumentNullException>(() => StateRevision.CreateDelta(Parent, null!, []));
        Assert.Throws<ArgumentNullException>(() => StateRevision.CreateDelta(Parent, [], null!));
        Assert.Throws<ArgumentException>(() => StateRevision.CreateBase(null, [null!], []));
        Assert.Throws<ArgumentException>(() => StateRevision.CreateDelta(Parent, [new(1, []), null!], []));
    }

    [Theory]
    [MemberData(nameof(InvalidObjectIdCollections))]
    public void ObjectId_collections_reject_zero_and_duplicates(Func<StateRevision> create) {
        Assert.ThrowsAny<ArgumentException>(() => create());
    }

    public static TheoryData<Func<StateRevision>> InvalidObjectIdCollections => new() {
        () => StateRevision.CreateBase(null, [new(1, []), new(1, [2])], []),
        () => StateRevision.CreateDelta(Parent, [], [0]),
        () => StateRevision.CreateDelta(Parent, [], [1, 1]),
        () => StateRevision.CreateBase(null, [], [new(0, Address(1, 32))]),
    };

    [Fact]
    public void Local_ids_must_not_overlap_membership_entries() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(null, [new(1, [])], [new(1, Address(1, 32))]));
        Assert.Throws<ArgumentException>(() => StateRevision.CreateDelta(Parent, [new(1, [])], [1]));
    }

    [Fact]
    public void External_heads_reject_duplicate_ids_and_empty_addresses() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(null, [], [new(1, Address(1, 32)), new(1, Address(1, 64))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => StateRevision.CreateBase(null, [], [new(1, default)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => StateRevision.CreateBase(default(FrameAddress), [], []));
    }

    private static FrameAddress Address(uint fileNumber, long offset) => new(fileNumber, SizedPtr.Create(offset, 32));
}
