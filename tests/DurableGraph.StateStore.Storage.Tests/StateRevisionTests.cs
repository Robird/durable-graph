using Atelia.Data;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class StateRevisionTests {
    private static readonly FrameAddress Parent = Address(1, 32);

    [Fact]
    public void Base_freezes_canonical_local_and_external_collections() {
        List<uint> baseIds = [9, 1];
        List<uint> deltaIds = [7, 3];
        Dictionary<uint, FrameAddress> external = new() {
            [8] = Address(1, 64),
            [2] = Address(1, 96),
        };

        StateRevision revision = StateRevision.CreateBase(
            Parent,
            baseIds,
            deltaIds,
            external);
        baseIds.Clear();
        deltaIds.Clear();
        external.Clear();

        Assert.Equal(ObjectHeadMapKind.Base, revision.ObjectHeadMapKind);
        Assert.Equal([1u, 9u], revision.BaseObjectIds);
        Assert.Equal([3u, 7u], revision.DeltaObjectIds);
        Assert.Equal([2u, 8u], revision.ExternalObjectHeads.Keys);
        Assert.Empty(revision.RemovedObjectIds);
    }

    [Fact]
    public void Delta_freezes_canonical_removed_ids_and_requires_parent() {
        List<uint> removed = [9, 2];

        StateRevision revision = StateRevision.CreateDelta(
            Parent,
            [3],
            [7],
            removed);
        removed.Clear();

        Assert.Equal(ObjectHeadMapKind.Delta, revision.ObjectHeadMapKind);
        Assert.Equal(Parent, revision.ParentRevisionAddress);
        Assert.Equal([2u, 9u], revision.RemovedObjectIds);
        Assert.Empty(revision.ExternalObjectHeads);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateRevision.CreateDelta(default, [], [], []));
    }

    [Theory]
    [MemberData(nameof(InvalidObjectIdCollections))]
    public void ObjectId_collections_reject_zero_and_duplicates(
        Func<StateRevision> create) {
        Assert.ThrowsAny<ArgumentException>(() => create());
    }

    public static TheoryData<Func<StateRevision>> InvalidObjectIdCollections => new() {
        () => StateRevision.CreateBase(null, [0], [], []),
        () => StateRevision.CreateBase(null, [1, 1], [], []),
        () => StateRevision.CreateBase(null, [], [0], []),
        () => StateRevision.CreateDelta(Parent, [], [], [0]),
        () => StateRevision.CreateBase(
            null,
            [],
            [],
            [new KeyValuePair<uint, FrameAddress>(0, Address(1, 32))]),
    };

    [Fact]
    public void Base_and_delta_object_ids_must_be_disjoint() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(null, [1], [1], []));
    }

    [Fact]
    public void Genesis_cannot_contain_Delta_ObjectVersions() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(null, [], [1], []));
    }

    [Fact]
    public void Local_ids_must_not_overlap_membership_entries() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(
                null,
                [1],
                [],
                [new KeyValuePair<uint, FrameAddress>(1, Address(1, 32))]));
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateDelta(Parent, [], [1], [1]));
    }

    [Fact]
    public void External_heads_reject_duplicate_ids_and_empty_addresses() {
        Assert.Throws<ArgumentException>(() =>
            StateRevision.CreateBase(
                null,
                [],
                [],
                [
                    new KeyValuePair<uint, FrameAddress>(1, Address(1, 32)),
                    new KeyValuePair<uint, FrameAddress>(1, Address(1, 64)),
                ]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateRevision.CreateBase(
                null,
                [],
                [],
                [new KeyValuePair<uint, FrameAddress>(1, default)]));
    }

    private static FrameAddress Address(uint fileNumber, long offset) =>
        new(fileNumber, SizedPtr.Create(offset, 32));
}
