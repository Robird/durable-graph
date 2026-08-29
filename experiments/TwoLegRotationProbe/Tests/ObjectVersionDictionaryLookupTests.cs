using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ObjectVersionDictionaryLookupTests {
    [Fact]
    public void Canonical_AA_BA_BB_lookup_uses_the_decisive_revision_scope() {
        const uint aa = 1;
        const uint ba = 2;
        const uint bb = 3;
        (RbfFileStore store, AbsoluteFrameAddress a, AbsoluteFrameAddress b) =
            BuildCanonicalAaBaBb(reverseBEntryOrder: false);

        ObjectVersionDictionaryLookupInspection inherited =
            ObjectVersionDictionaryReader.LookupLive(store, b, aa);
        ObjectVersionDictionaryLookupInspection localBa =
            ObjectVersionDictionaryReader.LookupLive(store, b, ba);
        ObjectVersionDictionaryLookupInspection localBb =
            ObjectVersionDictionaryReader.LookupLive(store, b, bb);

        AssertFound(inherited, aa, a, a, ObjectVersionDictionaryBindingKind.Self, [b, a]);
        AssertFound(localBa, ba, b, b, ObjectVersionDictionaryBindingKind.Self, [b]);
        AssertFound(localBb, bb, b, b, ObjectVersionDictionaryBindingKind.Self, [b]);

        IList<AbsoluteFrameAddress> readOnlyTrace =
            Assert.IsAssignableFrom<IList<AbsoluteFrameAddress>>(
                inherited.DictionaryRevisionAddresses);
        Assert.True(readOnlyTrace.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnlyTrace.Add(b));
    }

    [Fact]
    public void Self_is_decisive_without_resolving_a_missing_parent() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(new FrameTicket(4, 24)),
        };
        dictionary.BindSelf(objectId);
        FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };
        AddZeroByteBase(builder, objectId);
        AbsoluteFrameAddress revision = Append(file, builder);

        ObjectVersionDictionaryLookupInspection result =
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId);

        AssertFound(
            result,
            objectId,
            revision,
            revision,
            ObjectVersionDictionaryBindingKind.Self,
            [revision]);
    }

    [Fact]
    public void External_is_decisive_and_requires_the_same_object_id() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameBuilder targetBuilder = new();
        AddZeroByteBase(targetBuilder, objectId);
        AbsoluteFrameAddress target = Append(file, targetBuilder);
        ObjectVersionDictionaryBuilder dictionary = new();
        dictionary.BindExternal(objectId, Current(target.FrameTicket));
        AbsoluteFrameAddress revision = Append(
            file,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        ObjectVersionDictionaryLookupInspection result =
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId);

        AssertFound(
            result,
            objectId,
            revision,
            target,
            ObjectVersionDictionaryBindingKind.External,
            [revision]);

        RbfFileStore invalidStore = new();
        RbfFile invalidFile = invalidStore.CreateFile();
        FrameBuilder wrongTarget = new();
        AddZeroByteBase(wrongTarget, objectId + 1);
        AbsoluteFrameAddress wrongTargetAddress = Append(invalidFile, wrongTarget);
        ObjectVersionDictionaryBuilder invalidDictionary = new();
        invalidDictionary.BindExternal(objectId, Current(wrongTargetAddress.FrameTicket));
        AbsoluteFrameAddress invalidRevision = Append(
            invalidFile,
            new FrameBuilder { ObjectVersionDictionary = invalidDictionary });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(
                invalidStore,
                invalidRevision,
                objectId));
    }

    [Fact]
    public void Inherited_external_binding_resolves_in_its_source_revision_scope() {
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
        FrameBuilder bBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(aRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress bRevision = Append(bFile, bBuilder);

        ObjectVersionDictionaryLookupInspection result =
            ObjectVersionDictionaryReader.LookupLive(store, bRevision, objectId);

        AssertFound(
            result,
            objectId,
            aRevision,
            aTarget,
            ObjectVersionDictionaryBindingKind.External,
            [bRevision, aRevision]);
    }

    [Fact]
    public void Remove_is_definitive_without_reading_the_parent() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile previousFile = store.CreateFile();
        AbsoluteFrameAddress parentWithoutDictionary = Append(previousFile, new FrameBuilder());
        RbfFile currentFile = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(parentWithoutDictionary.FrameTicket),
        };
        dictionary.Remove(objectId);
        AbsoluteFrameAddress revision = Append(
            currentFile,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        ObjectVersionDictionaryLookupInspection result =
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId);

        Assert.Equal(ObjectVersionDictionaryLookupDisposition.Removed, result.Disposition);
        Assert.Equal(objectId, result.ObjectId);
        Assert.Equal(revision, result.DecisiveRevisionAddress);
        Assert.Equal(ObjectVersionDictionaryBindingKind.Remove, result.BindingKind);
        Assert.Null(result.ResolvedObjectVersionAddress);
        Assert.Equal([revision], result.DictionaryRevisionAddresses);
    }

    [Fact]
    public void Base_absence_is_definitive_even_when_it_has_a_lineage_parent() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile previousFile = store.CreateFile();
        FrameBuilder parentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        parentBuilder.ObjectVersionDictionary.BindSelf(objectId);
        AddZeroByteBase(parentBuilder, objectId);
        AbsoluteFrameAddress parent = Append(previousFile, parentBuilder);
        RbfFile currentFile = store.CreateFile();
        FrameBuilder baseBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Base,
                ParentRevisionFrameTicket = Previous(parent.FrameTicket),
            },
        };
        AbsoluteFrameAddress revision = Append(currentFile, baseBuilder);

        ObjectVersionDictionaryLookupInspection result =
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId);

        Assert.Equal(ObjectVersionDictionaryLookupDisposition.AbsentAtBase, result.Disposition);
        Assert.Equal(objectId, result.ObjectId);
        Assert.Equal(revision, result.DecisiveRevisionAddress);
        Assert.Null(result.BindingKind);
        Assert.Null(result.ResolvedObjectVersionAddress);
        Assert.Equal([revision], result.DictionaryRevisionAddresses);
    }

    [Fact]
    public void Revision_without_explicit_dictionary_fails_closed() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        AbsoluteFrameAddress revision = Append(file, new FrameBuilder());

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId: 7));
    }

    [Fact]
    public void Delta_missing_parent_frame_fails_closed() {
        RbfFileStore store = new();
        store.CreateFile();
        RbfFile file = store.CreateFile();
        FrameBuilder builder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(new FrameTicket(4, 24)),
            },
        };
        AbsoluteFrameAddress revision = Append(file, builder);

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId: 7));
    }

    [Fact]
    public void Non_earlier_parent_and_file_one_previous_parent_fail_closed() {
        RbfFileStore nonEarlierStore = new();
        RbfFile nonEarlierFile = nonEarlierStore.CreateFile();
        FrameBuilder nonEarlierBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Current(new FrameTicket(4, 24)),
            },
        };
        AbsoluteFrameAddress nonEarlierRevision = Append(nonEarlierFile, nonEarlierBuilder);

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(
                nonEarlierStore,
                nonEarlierRevision,
                objectId: 7));

        RbfFileStore fileOneStore = new();
        RbfFile fileOne = fileOneStore.CreateFile();
        FrameBuilder fileOneBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(new FrameTicket(4, 24)),
            },
        };
        AbsoluteFrameAddress fileOneRevision = Append(fileOne, fileOneBuilder);

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(
                fileOneStore,
                fileOneRevision,
                objectId: 7));
    }

    [Fact]
    public void External_binding_cannot_alias_its_containing_revision() {
        const uint objectId = 7;
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        ObjectVersionDictionaryBuilder dictionary = new();
        dictionary.BindExternal(objectId, Current(new FrameTicket(4, 24)));
        AbsoluteFrameAddress revision = Append(
            file,
            new FrameBuilder { ObjectVersionDictionary = dictionary });

        Assert.Throws<InvalidDataException>(() =>
            ObjectVersionDictionaryReader.LookupLive(store, revision, objectId));
    }

    [Fact]
    public void Lookup_is_repeatable_and_independent_of_entry_insertion_order() {
        (RbfFileStore firstStore, AbsoluteFrameAddress firstA, AbsoluteFrameAddress firstB) =
            BuildCanonicalAaBaBb(reverseBEntryOrder: false);
        (RbfFileStore secondStore, AbsoluteFrameAddress secondA, AbsoluteFrameAddress secondB) =
            BuildCanonicalAaBaBb(reverseBEntryOrder: true);

        ObjectVersionDictionaryLookupInspection first =
            ObjectVersionDictionaryReader.LookupLive(firstStore, firstB, objectId: 1);
        ObjectVersionDictionaryLookupInspection repeated =
            ObjectVersionDictionaryReader.LookupLive(firstStore, firstB, objectId: 1);
        ObjectVersionDictionaryLookupInspection reordered =
            ObjectVersionDictionaryReader.LookupLive(secondStore, secondB, objectId: 1);

        AssertFound(
            first,
            objectId: 1,
            firstA,
            firstA,
            ObjectVersionDictionaryBindingKind.Self,
            [firstB, firstA]);
        AssertEquivalent(first, repeated);
        AssertEquivalent(first, reordered);
        Assert.Equal(firstA, secondA);
        Assert.Equal(firstB, secondB);
    }

    [Fact]
    public void Inspection_rejects_incoherent_outcomes_and_traces() {
        AbsoluteFrameAddress decisive = new(1, new FrameTicket(32, 24));
        AbsoluteFrameAddress resolved = new(1, new FrameTicket(4, 24));

        Action[] invalidConstructions = [
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                (ObjectVersionDictionaryLookupDisposition)255,
                decisive,
                ObjectVersionDictionaryBindingKind.Self,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Found,
                decisive,
                (ObjectVersionDictionaryBindingKind)255,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Found,
                decisive,
                ObjectVersionDictionaryBindingKind.Remove,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Found,
                decisive,
                ObjectVersionDictionaryBindingKind.Self,
                resolvedObjectVersionAddress: null,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Found,
                decisive,
                ObjectVersionDictionaryBindingKind.Self,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Found,
                decisive,
                ObjectVersionDictionaryBindingKind.External,
                decisive,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Removed,
                decisive,
                ObjectVersionDictionaryBindingKind.Self,
                resolvedObjectVersionAddress: null,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.Removed,
                decisive,
                ObjectVersionDictionaryBindingKind.Remove,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
                decisive,
                ObjectVersionDictionaryBindingKind.Self,
                resolvedObjectVersionAddress: null,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
                decisive,
                bindingKind: null,
                resolved,
                [decisive]),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
                decisive,
                bindingKind: null,
                resolvedObjectVersionAddress: null,
                []),
            () => new ObjectVersionDictionaryLookupInspection(
                1,
                ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
                decisive,
                bindingKind: null,
                resolvedObjectVersionAddress: null,
                [resolved]),
        ];

        foreach (Action invalidConstruction in invalidConstructions) {
            Assert.ThrowsAny<ArgumentException>(invalidConstruction);
        }
    }

    private static (
        RbfFileStore Store,
        AbsoluteFrameAddress A,
        AbsoluteFrameAddress B) BuildCanonicalAaBaBb(bool reverseBEntryOrder) {
        const uint aa = 1;
        const uint ba = 2;
        const uint bb = 3;
        RbfFileStore store = new();
        RbfFile aFile = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(aa);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddZeroByteBase(aBuilder, aa);
        AbsoluteFrameAddress a = Append(aFile, aBuilder);

        RbfFile bFile = store.CreateFile();
        ObjectVersionDictionaryBuilder bDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        if (reverseBEntryOrder) {
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

    private static void AssertFound(
        ObjectVersionDictionaryLookupInspection result,
        uint objectId,
        AbsoluteFrameAddress decisiveRevisionAddress,
        AbsoluteFrameAddress resolvedObjectVersionAddress,
        ObjectVersionDictionaryBindingKind bindingKind,
        AbsoluteFrameAddress[] dictionaryRevisionAddresses) {
        Assert.Equal(ObjectVersionDictionaryLookupDisposition.Found, result.Disposition);
        Assert.Equal(objectId, result.ObjectId);
        Assert.Equal(decisiveRevisionAddress, result.DecisiveRevisionAddress);
        Assert.Equal(bindingKind, result.BindingKind);
        Assert.Equal(resolvedObjectVersionAddress, result.ResolvedObjectVersionAddress);
        Assert.Equal(dictionaryRevisionAddresses, result.DictionaryRevisionAddresses);
    }

    private static void AssertEquivalent(
        ObjectVersionDictionaryLookupInspection expected,
        ObjectVersionDictionaryLookupInspection actual) {
        Assert.Equal(expected.ObjectId, actual.ObjectId);
        Assert.Equal(expected.Disposition, actual.Disposition);
        Assert.Equal(expected.DecisiveRevisionAddress, actual.DecisiveRevisionAddress);
        Assert.Equal(expected.BindingKind, actual.BindingKind);
        Assert.Equal(expected.ResolvedObjectVersionAddress, actual.ResolvedObjectVersionAddress);
        Assert.Equal(expected.DictionaryRevisionAddresses, actual.DictionaryRevisionAddresses);
    }

    private static void AddZeroByteBase(FrameBuilder builder, uint objectId) =>
        builder.Add(objectId).ReconstructionObjectPayloadBytes = 0;

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);
}
