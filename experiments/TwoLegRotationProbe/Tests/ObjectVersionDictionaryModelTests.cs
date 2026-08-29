using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ObjectVersionDictionaryModelTests {
    private static readonly RelativeFrameTicket ParentRevision = new(
        IsPreviousFile: true,
        FrameTicket: new FrameTicket(4, 24));

    [Fact]
    public void Frame_without_explicit_dictionary_preserves_unknown_legacy_state() {
        Frame frame = new FrameBuilder().Build();

        Assert.Null(frame.ObjectVersionDictionary);
    }

    [Fact]
    public void Build_freezes_dictionary_kind_parent_and_entries() {
        const uint objectId = 11;
        FrameBuilder frameBuilder = new();
        frameBuilder.Add(objectId).ReconstructionObjectPayloadBytes = 0;
        ObjectVersionDictionaryBuilder dictionaryBuilder = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = ParentRevision,
        };
        dictionaryBuilder.BindSelf(objectId);
        frameBuilder.ObjectVersionDictionary = dictionaryBuilder;

        Frame frame = frameBuilder.Build();
        dictionaryBuilder.Kind = ObjectVersionDictionaryKind.Delta;
        dictionaryBuilder.ParentRevisionFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: false,
            FrameTicket: new FrameTicket(32, 24));
        dictionaryBuilder.BindExternal(12, ParentRevision);
        frameBuilder.ObjectVersionDictionary = null;

        ObjectVersionDictionary dictionary = Assert.IsType<ObjectVersionDictionary>(
            frame.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Base, dictionary.Kind);
        Assert.Equal(ParentRevision, dictionary.ParentRevisionFrameTicket);
        KeyValuePair<uint, ObjectVersionDictionaryBinding> entry = Assert.Single(dictionary.Entries);
        Assert.Equal(objectId, entry.Key);
        Assert.Equal(ObjectVersionDictionaryBindingKind.Self, entry.Value.Kind);
        Assert.Null(entry.Value.ExternalFrameTicket);
        Assert.False(dictionary.Entries.ContainsKey(12));

        IDictionary<uint, ObjectVersionDictionaryBinding> entries =
            Assert.IsAssignableFrom<IDictionary<uint, ObjectVersionDictionaryBinding>>(
                dictionary.Entries);
        Assert.True(entries.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => entries.Add(13, ObjectVersionDictionaryBinding.Remove()));
    }

    [Fact]
    public void Delta_requires_parent_while_base_may_have_or_omit_one() {
        ObjectVersionDictionary withoutParent = new(
            ObjectVersionDictionaryKind.Base,
            parentRevisionFrameTicket: null,
            []);
        ObjectVersionDictionary withParent = new(
            ObjectVersionDictionaryKind.Base,
            ParentRevision,
            []);

        Assert.Null(withoutParent.ParentRevisionFrameTicket);
        Assert.Equal(ParentRevision, withParent.ParentRevisionFrameTicket);
        Assert.Throws<ArgumentException>(
            () => new ObjectVersionDictionary(
                ObjectVersionDictionaryKind.Delta,
                parentRevisionFrameTicket: null,
                []));
    }

    [Fact]
    public void Base_rejects_remove_but_delta_accepts_it() {
        KeyValuePair<uint, ObjectVersionDictionaryBinding>[] removal = [
            KeyValuePair.Create(7U, ObjectVersionDictionaryBinding.Remove()),
        ];

        Assert.Throws<ArgumentException>(
            () => new ObjectVersionDictionary(
                ObjectVersionDictionaryKind.Base,
                ParentRevision,
                removal));

        ObjectVersionDictionary delta = new(
            ObjectVersionDictionaryKind.Delta,
            ParentRevision,
            removal);
        Assert.Equal(ObjectVersionDictionaryBindingKind.Remove, delta.Entries[7].Kind);
    }

    [Fact]
    public void Dictionary_rejects_invalid_kind_binding_shapes_and_duplicate_ids() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ObjectVersionDictionary(
                (ObjectVersionDictionaryKind)255,
                ParentRevision,
                []));

        ObjectVersionDictionaryBinding[] invalidBindings = [
            default,
            new(ObjectVersionDictionaryBindingKind.Self, ParentRevision),
            new(ObjectVersionDictionaryBindingKind.External, null),
            new(ObjectVersionDictionaryBindingKind.Remove, ParentRevision),
        ];
        foreach (ObjectVersionDictionaryBinding invalidBinding in invalidBindings) {
            Assert.ThrowsAny<ArgumentException>(
                () => new ObjectVersionDictionary(
                    ObjectVersionDictionaryKind.Delta,
                    ParentRevision,
                    [KeyValuePair.Create(1U, invalidBinding)]));
        }

        KeyValuePair<uint, ObjectVersionDictionaryBinding>[] duplicates = [
            KeyValuePair.Create(1U, ObjectVersionDictionaryBinding.BindSelf()),
            KeyValuePair.Create(1U, ObjectVersionDictionaryBinding.BindExternal(ParentRevision)),
        ];
        Assert.Throws<ArgumentException>(
            () => new ObjectVersionDictionary(
                ObjectVersionDictionaryKind.Base,
                ParentRevision,
                duplicates));
    }

    [Fact]
    public void Frame_requires_every_self_binding_to_have_a_same_object_record() {
        ObjectVersionDictionaryBuilder dictionary = new();
        dictionary.BindSelf(7);
        FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };

        Assert.Throws<ArgumentException>(() => builder.Build());

        builder.Add(7).ReconstructionObjectPayloadBytes = 0;
        Frame frame = builder.Build();
        Assert.True(frame.ObjectVersions.ContainsKey(7));
        Assert.NotNull(frame.ObjectVersionDictionary);
    }

    [Fact]
    public void Frame_allows_domain_records_without_self_bindings() {
        FrameBuilder builder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        builder.Add(7).ReconstructionObjectPayloadBytes = 0;

        Frame frame = builder.Build();

        Assert.True(frame.ObjectVersions.ContainsKey(7));
        Assert.Empty(Assert.IsType<ObjectVersionDictionary>(frame.ObjectVersionDictionary).Entries);
    }
}
