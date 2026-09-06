using Atelia.Data;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class LiveObjectHeadMapMaterializerTests {
    [Fact]
    public void Base_binds_local_records_to_self_and_preserves_external_heads() {
        FrameAddress parent = Address(1, 32);
        FrameAddress externalHead = Address(1, 96);
        FrameAddress checkpoint = Address(2, 32);
        StateRevision revision = StateRevision.CreateBase(
            parent,
            localObjects: Records(3, 1),
            externalObjectHeads: [
                new KeyValuePair<uint, FrameAddress>(2, externalHead),
            ]);

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                checkpoint,
                address => address == checkpoint
                    ? revision
                    : throw new KeyNotFoundException());

        AssertHeads(
            heads,
            (1, checkpoint),
            (2, externalHead),
            (3, checkpoint));
    }

    [Fact]
    public void Delta_inherits_updates_adds_and_removes_against_exact_parent() {
        FrameAddress f1 = Address(1, 32);
        FrameAddress f2 = Address(1, 96);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [f1] = StateRevision.CreateBase(
                null,
                localObjects: Records(1, 2, 3),
                externalObjectHeads: []),
            [f2] = StateRevision.CreateDelta(
                f1,
                localObjects: Records(4, 1),
                removedObjectIds: [2]),
        };

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                f2,
                revisions.GetValueOrDefault!);

        AssertHeads(heads, (1, f2), (3, f1), (4, f2));
    }

    [Fact]
    public void Empty_Delta_preserves_every_inherited_head() {
        FrameAddress externalHead = Address(1, 32);
        FrameAddress checkpoint = Address(2, 32);
        FrameAddress noChange = Address(2, 96);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [checkpoint] = StateRevision.CreateBase(
                externalHead,
                localObjects: Records(2),
                externalObjectHeads: [
                    new KeyValuePair<uint, FrameAddress>(1, externalHead),
                ]),
            [noChange] = StateRevision.CreateDelta(
                checkpoint,
                localObjects: Records(),
                removedObjectIds: []),
        };

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                noChange,
                revisions.GetValueOrDefault!);

        AssertHeads(heads, (1, externalHead), (2, checkpoint));
    }

    [Fact]
    public void Repeated_local_overwrite_moves_only_that_Object_head() {
        FrameAddress f1 = Address(1, 32);
        FrameAddress f2 = Address(1, 96);
        FrameAddress f3 = Address(1, 160);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [f1] = StateRevision.CreateBase(null, Records(1, 2), []),
            [f2] = StateRevision.CreateDelta(f1, Records(1), []),
            [f3] = StateRevision.CreateDelta(f2, Records(1), []),
        };

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                f3,
                revisions.GetValueOrDefault!);

        AssertHeads(heads, (1, f3), (2, f1));
    }

    [Fact]
    public void Local_occurrence_after_Remove_makes_the_Object_live_again() {
        FrameAddress f1 = Address(1, 32);
        FrameAddress f2 = Address(1, 96);
        FrameAddress f3 = Address(1, 160);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [f1] = StateRevision.CreateBase(null, Records(1, 2), []),
            [f2] = StateRevision.CreateDelta(f1, Records(), [1]),
            [f3] = StateRevision.CreateDelta(f2, Records(1), []),
        };

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                f3,
                revisions.GetValueOrDefault!);

        AssertHeads(heads, (1, f3), (2, f1));
    }

    [Fact]
    public void Non_genesis_Base_is_complete_and_does_not_read_its_parent() {
        FrameAddress missingParent = Address(1, 32);
        FrameAddress checkpoint = Address(2, 32);
        StateRevision revision = StateRevision.CreateBase(
            missingParent,
            localObjects: Records(3, 1),
            externalObjectHeads: [
                new KeyValuePair<uint, FrameAddress>(2, Address(1, 96)),
            ]);
        List<FrameAddress> reads = [];

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                checkpoint,
                address => {
                    reads.Add(address);
                    return address == checkpoint
                        ? revision
                        : throw new KeyNotFoundException();
                });

        AssertHeads(
            heads,
            (1, checkpoint),
            (2, Address(1, 96)),
            (3, checkpoint));
        Assert.Equal([checkpoint], reads);
    }

    [Fact]
    public void Result_enumerates_in_ObjectId_order_and_is_immutable() {
        FrameAddress head = Address(1, 32);
        StateRevision revision = StateRevision.CreateBase(
            null,
            localObjects: Records(3, 1, 2),
            externalObjectHeads: []);

        IReadOnlyDictionary<uint, FrameAddress> heads =
            LiveObjectHeadMapMaterializer.Materialize(
                head,
                address => address == head
                    ? revision
                    : throw new KeyNotFoundException());

        Assert.Equal([1u, 2u, 3u], heads.Keys);
        IDictionary<uint, FrameAddress> mutableView =
            Assert.IsAssignableFrom<IDictionary<uint, FrameAddress>>(heads);
        Assert.Throws<NotSupportedException>(() =>
            mutableView.Add(4, head));
    }

    [Fact]
    public void Acyclic_future_parent_fails_closed() {
        FrameAddress f1 = Address(1, 32);
        FrameAddress f2 = Address(1, 96);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [f1] = StateRevision.CreateDelta(f2, Records(), []),
            [f2] = StateRevision.CreateBase(null, Records(1), []),
        };

        Assert.Throws<InvalidDataException>(() =>
            LiveObjectHeadMapMaterializer.Materialize(
                f1,
                revisions.GetValueOrDefault!));
    }

    [Fact]
    public void Future_external_head_fails_closed() {
        FrameAddress checkpoint = Address(1, 32);
        StateRevision revision = StateRevision.CreateBase(
            null,
            localObjects: Records(),
            externalObjectHeads: [
                new KeyValuePair<uint, FrameAddress>(1, Address(1, 96)),
            ]);

        Assert.Throws<InvalidDataException>(() =>
            LiveObjectHeadMapMaterializer.Materialize(
                checkpoint,
                address => address == checkpoint
                    ? revision
                    : throw new KeyNotFoundException()));
    }

    [Fact]
    public void Parent_cycle_fails_closed_at_a_non_earlier_edge() {
        FrameAddress f1 = Address(1, 32);
        FrameAddress f2 = Address(1, 96);
        Dictionary<FrameAddress, StateRevision> revisions = new() {
            [f1] = StateRevision.CreateDelta(f2, Records(), []),
            [f2] = StateRevision.CreateDelta(f1, Records(), []),
        };

        Assert.Throws<InvalidDataException>(() =>
            LiveObjectHeadMapMaterializer.Materialize(
                f2,
                revisions.GetValueOrDefault!));
    }

    private static void AssertHeads(
        IReadOnlyDictionary<uint, FrameAddress> actual,
        params (uint ObjectId, FrameAddress Head)[] expected) {
        Assert.Equal(
            expected,
            actual.Select(pair => (pair.Key, pair.Value)).ToArray());
    }

    private static ObjectVersionRecord[] Records(params uint[] ids) =>
        ids.Select(id => ObjectVersionRecord.CreateBase(id, [(byte)id])).ToArray();

    private static FrameAddress Address(uint fileNumber, long offset) =>
        new(fileNumber, SizedPtr.Create(offset, 32));
}
