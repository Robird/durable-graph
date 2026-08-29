using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ObjectVersionDictionaryMaterializationTests {
    [Fact]
    public void Canonical_AA_BA_BB_chain_materializes_one_sorted_live_map() {
        (RbfFileStore store, AbsoluteFrameAddress a, AbsoluteFrameAddress b) =
            BuildCanonicalAaBaBb(reverseEntryOrder: false);

        ObjectVersionDictionaryMaterializationInspection result =
            ObjectVersionDictionaryReader.MaterializeLive(store, b);

        Assert.Equal(b, result.HeadRevisionAddress);
        Assert.Equal([b, a], result.DictionaryRevisionAddresses);
        Assert.Equal([1u, 2u, 3u], result.Bindings.Keys);
        Assert.Equal(a, result.Bindings[1]);
        Assert.Equal(b, result.Bindings[2]);
        Assert.Equal(b, result.Bindings[3]);

        IDictionary<uint, AbsoluteFrameAddress> readOnlyBindings =
            Assert.IsAssignableFrom<IDictionary<uint, AbsoluteFrameAddress>>(
                result.Bindings);
        Assert.True(readOnlyBindings.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnlyBindings.Add(4, b));

        IList<AbsoluteFrameAddress> readOnlyTrace =
            Assert.IsAssignableFrom<IList<AbsoluteFrameAddress>>(
                result.DictionaryRevisionAddresses);
        Assert.True(readOnlyTrace.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnlyTrace.Add(b));
    }

    [Fact]
    public void Remove_and_empty_Delta_replay_from_the_first_Base() {
        const uint removed = 7;
        const uint retained = 8;
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(removed);
        aDictionary.BindSelf(retained);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddZeroByteBase(aBuilder, removed);
        AddZeroByteBase(aBuilder, retained);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = DeltaFromPrevious(a);
        bDictionary.Remove(removed);
        AbsoluteFrameAddress b = Append(
            bFile,
            new FrameBuilder { ObjectVersionDictionary = bDictionary });

        RbfFile cFile = store.CreateFile();
        AbsoluteFrameAddress c = Append(
            cFile,
            new FrameBuilder {
                ObjectVersionDictionary = DeltaFromPrevious(b),
            });

        ObjectVersionDictionaryMaterializationInspection result =
            ObjectVersionDictionaryReader.MaterializeLive(store, c);

        Assert.Equal([c, b, a], result.DictionaryRevisionAddresses);
        KeyValuePair<uint, AbsoluteFrameAddress> binding = Assert.Single(result.Bindings);
        Assert.Equal(retained, binding.Key);
        Assert.Equal(a, binding.Value);
    }

    [Fact]
    public void Inherited_External_binding_resolves_in_its_source_revision_scope() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        FrameBuilder targetBuilder = new();
        AddZeroByteBase(targetBuilder, objectId);
        AbsoluteFrameAddress aTarget = Append(aFile, targetBuilder);
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindExternal(objectId, Current(aTarget.FrameTicket));
        AbsoluteFrameAddress aRevision = Append(
            aFile,
            new FrameBuilder { ObjectVersionDictionary = aDictionary });

        RbfFile bFile = store.CreateFile();
        AbsoluteFrameAddress bRevision = Append(
            bFile,
            new FrameBuilder {
                ObjectVersionDictionary = DeltaFromPrevious(aRevision),
            });

        ObjectVersionDictionaryMaterializationInspection result =
            ObjectVersionDictionaryReader.MaterializeLive(store, bRevision);

        Assert.Equal(aTarget, Assert.Single(result.Bindings).Value);
        Assert.Equal([bRevision, aRevision], result.DictionaryRevisionAddresses);
    }

    [Fact]
    public void Base_parent_is_lineage_only_and_is_not_read_or_validated() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = Previous(new FrameTicket(4, 24)),
        };
        AbsoluteFrameAddress revision = Append(
            file,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        ObjectVersionDictionaryMaterializationInspection result =
            ObjectVersionDictionaryReader.MaterializeLive(store, revision);

        Assert.Empty(result.Bindings);
        Assert.Equal([revision], result.DictionaryRevisionAddresses);
    }

    [Fact]
    public void Missing_head_or_parent_Revision_fails_closed() {
        RbfFileStore missingHeadStore = new();
        RbfFile missingHeadFile = missingHeadStore.CreateFile();
        AbsoluteFrameAddress missingHead = new(
            missingHeadFile.FileNumber,
            new FrameTicket(4, 24));

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.MaterializeLive(missingHeadStore, missingHead));

        RbfFileStore missingParentStore = new();
        missingParentStore.CreateFile();
        RbfFile currentFile = missingParentStore.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(new FrameTicket(4, 24)),
        };
        AbsoluteFrameAddress revision = Append(
            currentFile,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.MaterializeLive(
                missingParentStore,
                revision));
    }

    [Fact]
    public void Non_earlier_parent_back_edge_fails_closed() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Current(new FrameTicket(4, 24)),
        };
        AbsoluteFrameAddress revision = Append(
            file,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.MaterializeLive(store, revision));
    }

    [Fact]
    public void Delta_without_parent_is_rejected_before_materialization() {
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
        };

        Assert.Throws<ArgumentException>(dictionary.Build);
    }

    [Fact]
    public void External_binding_requires_an_earlier_frame_with_the_same_object_id() {
        const uint objectId = 7;
        RbfFileStore missingObjectStore = new();
        RbfFile missingObjectFile = missingObjectStore.CreateFile();
        AbsoluteFrameAddress wrongTarget = Append(
            missingObjectFile,
            new FrameBuilder());
        ObjectVersionDictionaryBuilder missingObjectDictionary = new();
        missingObjectDictionary.BindExternal(
            objectId,
            Current(wrongTarget.FrameTicket));
        AbsoluteFrameAddress missingObjectRevision = Append(
            missingObjectFile,
            new FrameBuilder {
                ObjectVersionDictionary = missingObjectDictionary,
            });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.MaterializeLive(
                missingObjectStore,
                missingObjectRevision));

        RbfFileStore nonEarlierStore = new();
        RbfFile nonEarlierFile = nonEarlierStore.CreateFile();
        ObjectVersionDictionaryBuilder nonEarlierDictionary = new();
        nonEarlierDictionary.BindExternal(
            objectId,
            Current(new FrameTicket(4, 24)));
        AbsoluteFrameAddress nonEarlierRevision = Append(
            nonEarlierFile,
            new FrameBuilder {
                ObjectVersionDictionary = nonEarlierDictionary,
            });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.MaterializeLive(
                nonEarlierStore,
                nonEarlierRevision));
    }

    [Fact]
    public void Materialization_is_repeatable_and_independent_of_entry_insertion_order() {
        (RbfFileStore firstStore, _, AbsoluteFrameAddress firstHead) =
            BuildCanonicalAaBaBb(reverseEntryOrder: false);
        (RbfFileStore reorderedStore, _, AbsoluteFrameAddress reorderedHead) =
            BuildCanonicalAaBaBb(reverseEntryOrder: true);

        ObjectVersionDictionaryMaterializationInspection first =
            ObjectVersionDictionaryReader.MaterializeLive(firstStore, firstHead);
        ObjectVersionDictionaryMaterializationInspection repeated =
            ObjectVersionDictionaryReader.MaterializeLive(firstStore, firstHead);
        ObjectVersionDictionaryMaterializationInspection reordered =
            ObjectVersionDictionaryReader.MaterializeLive(
                reorderedStore,
                reorderedHead);

        Assert.Equal(first.Bindings.ToArray(), repeated.Bindings.ToArray());
        Assert.Equal(first.DictionaryRevisionAddresses, repeated.DictionaryRevisionAddresses);
        Assert.Equal(first.Bindings.ToArray(), reordered.Bindings.ToArray());
        Assert.Equal(first.DictionaryRevisionAddresses, reordered.DictionaryRevisionAddresses);
    }

    [Fact]
    public void Inspection_rejects_duplicate_bindings_and_invalid_traces() {
        AbsoluteFrameAddress head = new(1, new FrameTicket(32, 24));
        KeyValuePair<uint, AbsoluteFrameAddress>[] duplicateBindings = [
            new(1, head),
            new(1, head),
        ];

        Assert.Throws<ArgumentException>(() =>
            new ObjectVersionDictionaryMaterializationInspection(
                head,
                duplicateBindings,
                [head]));
        Assert.Throws<ArgumentException>(() =>
            new ObjectVersionDictionaryMaterializationInspection(
                head,
                [],
                []));
        Assert.Throws<ArgumentException>(() =>
            new ObjectVersionDictionaryMaterializationInspection(
                head,
                [],
                [new AbsoluteFrameAddress(1, new FrameTicket(4, 24))]));
    }

    private static (
        RbfFileStore Store,
        AbsoluteFrameAddress A,
        AbsoluteFrameAddress B) BuildCanonicalAaBaBb(bool reverseEntryOrder) {
        const uint aa = 1;
        const uint ba = 2;
        const uint bb = 3;
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        if (reverseEntryOrder) {
            aDictionary.BindSelf(ba);
            aDictionary.BindSelf(aa);
        } else {
            aDictionary.BindSelf(aa);
            aDictionary.BindSelf(ba);
        }

        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddZeroByteBase(aBuilder, aa);
        AddZeroByteBase(aBuilder, ba);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = DeltaFromPrevious(a);
        if (reverseEntryOrder) {
            bDictionary.BindSelf(bb);
            bDictionary.BindSelf(ba);
        } else {
            bDictionary.BindSelf(ba);
            bDictionary.BindSelf(bb);
        }

        FrameBuilder bBuilder = new() { ObjectVersionDictionary = bDictionary };
        AddZeroByteBase(bBuilder, ba);
        AddZeroByteBase(bBuilder, bb);
        AbsoluteFrameAddress b = Append(bFile, bBuilder);
        return (store, a, b);
    }

    private static ObjectVersionDictionaryBuilder DeltaFromPrevious(
        AbsoluteFrameAddress parent) => new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(parent.FrameTicket),
        };

    private static void AddZeroByteBase(FrameBuilder builder, uint objectId) =>
        builder.Add(objectId).ReconstructionObjectPayloadBytes = 0;

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);
}
