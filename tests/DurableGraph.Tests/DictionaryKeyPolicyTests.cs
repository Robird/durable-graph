namespace Atelia.DurableGraph.Tests;

public sealed class DictionaryKeyPolicyTests {
    private static readonly DictionaryLayout Strings = new(new(1, TypeTag.String), new(2, TypeTag.Int32));

    [Fact]
    public void KnownStringComparersNormalizeWithoutCallingUnknownCode() {
        Assert.Equal(DictionaryComparerKind.StringOrdinal, DictionaryKeyPolicy.Identify(EqualityComparer<string>.Default, Strings.KeySlot));
        Assert.Equal(DictionaryComparerKind.StringOrdinal, DictionaryKeyPolicy.Identify<string>(StringComparer.Ordinal, Strings.KeySlot));
        Assert.Equal(DictionaryComparerKind.StringOrdinalIgnoreCase, DictionaryKeyPolicy.Identify<string>(StringComparer.OrdinalIgnoreCase, Strings.KeySlot));
        Assert.Equal(DictionaryComparerKind.ReferenceIdentity, DictionaryKeyPolicy.Identify<string>(ReferenceEqualityComparer.Instance, Strings.KeySlot));
        Assert.Throws<InvalidDataException>(() => DictionaryKeyPolicy.Identify(new HostileComparer(), Strings.KeySlot));
        Assert.Throws<InvalidDataException>(() => DictionaryKeyPolicy.Identify<string>(StringComparer.InvariantCulture, Strings.KeySlot));
        Assert.Same(StringComparer.Ordinal, DictionaryKeyPolicy.CreateComparer<string>(DictionaryComparerKind.StringOrdinal, Strings.KeySlot));
    }

    [Fact]
    public void DomainReferenceDefaultIsRejectedButExplicitIdentityIsAccepted() {
        DurableFieldInfo slot = DurableFieldInfo.Reference(1, TypeExpr.Named("Node"));
        Assert.Throws<InvalidDataException>(() => DictionaryKeyPolicy.Identify(EqualityComparer<KeyNode>.Default, slot));
        Assert.Equal(DictionaryComparerKind.ReferenceIdentity, DictionaryKeyPolicy.Identify<KeyNode>(ReferenceEqualityComparer.Instance, slot));
        Assert.Same(ReferenceEqualityComparer.Instance, DictionaryKeyPolicy.CreateComparer<KeyNode>(DictionaryComparerKind.ReferenceIdentity, slot));
    }

    [Fact]
    public void StructAndNullableCurrentKeysRequireFutureComparisonContract() {
        DurableSchema inline = new("Point", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableFieldInfo inlineSlot = new(1, TypeTag.InlineValue, inlineSchema: inline);
        Assert.Throws<ArgumentException>(() => DictionaryKeyPolicy.RequireCurrentKey(typeof(OrdinaryStruct), inlineSlot));
        Assert.Throws<ArgumentException>(() => DictionaryKeyPolicy.RequireCurrentKey(typeof(int?), DurableFieldInfo.Nullable(1, new(1, TypeTag.Int32))));
        Assert.Throws<InvalidDataException>(() => DictionaryKeyPolicy.RequireStoredPolicy(DictionaryComparerKind.ScalarDefault,
            DurableFieldInfo.Nullable(1, new(1, TypeTag.Int32))));
    }

    [Fact]
    public void HistoricalSingleIntegerRepresentationNeedsNoOldDomainEnumType() {
        DurableSchema schema = new("DeletedEnum", 7, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DictionaryLayout layout = new(new(1, TypeTag.InlineValue, inlineSchema: schema), new(2, TypeTag.Int32));
        FrozenDictionaryState<int, int> state = new(DictionaryComparerKind.ScalarDefault,
            new DictionaryEntryState<int, int>[] { new(int.MinValue, 1), new(123456, 2) });
        var prepared = DictionaryStateBody<int, int, Int32StateOps, Int32StateOps>.PrepareBase(state, layout);
        Assert.Equal(new byte[] { 0, 2, 255, 255, 255, 255, 15, 2, 128, 137, 15, 4 }, prepared.Body.ToArray());
        DictionaryStateBody<int, int, Int32StateOps, Int32StateOps>.ValidateLookupKeys(state, layout,
            _ => throw new InvalidOperationException("Scalar validation must not resolve graph objects."));
        Assert.Equal(DictionaryComparerKind.ScalarDefault, DictionaryKeyPolicy.Identify(EqualityComparer<CurrentEnum>.Default, layout.KeySlot));
    }

    [Fact]
    public void HistoricalEnumShapeValidationRejectsExtraFieldsNonIntegerAndGenericLayouts() {
        DurableSchema[] invalid = [
            new("A", 1, SchemaKind.InlineValue, new DurableFieldInfo(2, TypeTag.Int32)),
            new("A", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Double)),
            new("A", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32), new DurableFieldInfo(2, TypeTag.Int32)),
            new(TypeExpr.Named("A", TypeExpr.Builtin(TypeTag.Int32)), 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32)),
        ];
        foreach (DurableSchema schema in invalid) {
            Assert.Throws<InvalidDataException>(() => DictionaryKeyPolicy.RequireStoredPolicy(DictionaryComparerKind.ScalarDefault,
                new(1, TypeTag.InlineValue, inlineSchema: schema)));
        }
    }

    [Fact]
    public void FloatingLookupEqualityRejectsSignedZeroAndDistinctNaNPayloads() {
        DictionaryLayout layout = new(new(1, TypeTag.Single), new(2, TypeTag.Int32));
        FrozenDictionaryState<float, int> zeros = new(DictionaryComparerKind.ScalarDefault,
            new DictionaryEntryState<float, int>[] { new(0f, 1), new(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), 2) });
        FrozenDictionaryState<float, int> nans = new(DictionaryComparerKind.ScalarDefault,
            new DictionaryEntryState<float, int>[] { new(BitConverter.Int32BitsToSingle(0x7FC00001), 1), new(BitConverter.Int32BitsToSingle(0x7FC00002), 2) });
        Assert.Throws<InvalidDataException>(() => DictionaryStateBody<float, int, SingleStateOps, Int32StateOps>.ValidateLocal(zeros, layout));
        Assert.Throws<InvalidDataException>(() => DictionaryStateBody<float, int, SingleStateOps, Int32StateOps>.ValidateLocal(nans, layout));
        FrozenDictionaryState<float, int> prior = new(DictionaryComparerKind.ScalarDefault, zeros.Entries[..1]);
        FrozenDictionaryState<float, int> current = new(DictionaryComparerKind.ScalarDefault, zeros.Entries[1..]);
        var delta = DictionaryStateBody<float, int, SingleStateOps, Int32StateOps>.PrepareDelta(prior, current, layout);
        Assert.True(delta.HasChanges);
        // One removal, no Patch, one addition; the original key bits are not retained.
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 128, 4 }, delta.Body.ToArray());
    }

    [Theory]
    [InlineData(DictionaryComparerKind.StringOrdinal, "same", "same", true)]
    [InlineData(DictionaryComparerKind.StringOrdinal, "a", "A", false)]
    [InlineData(DictionaryComparerKind.StringOrdinalIgnoreCase, "a", "A", true)]
    [InlineData(DictionaryComparerKind.ReferenceIdentity, "same", "same", false)]
    [InlineData(DictionaryComparerKind.ReferenceIdentity, "", "", true)]
    public void CompleteGraphValidationUsesRecordedStringPolicyAndCanonicalEmpty(DictionaryComparerKind kind, string first, string second, bool rejects) {
        FrozenDictionaryState<ObjectId, int> state = StringState(kind, 1, 2);
        Dictionary<ObjectId, ObjectStateRecord> rows = new() { [new(1)] = new(new(1), new string(first.ToCharArray())), [new(2)] = new(new(2), new string(second.ToCharArray())) };
        DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.ValidateLocal(state, Strings);
        if (rejects) {
            Assert.Throws<InvalidDataException>(() => DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.ValidateLookupKeys(state, Strings, id => rows[id]));
        } else {
            DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.ValidateLookupKeys(state, Strings, id => rows[id]);
        }
    }

    [Fact]
    public void NullCanonicalDuplicatesAndComparisonPolicyChangesReject() {
        Assert.Throws<InvalidDataException>(() => DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.ValidateLocal(
            StringState(DictionaryComparerKind.ReferenceIdentity, 0), Strings));
        Assert.Throws<InvalidDataException>(() => DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.ValidateLocal(
            StringState(DictionaryComparerKind.ReferenceIdentity, 1, 1), Strings));
        Assert.Throws<InvalidDataException>(() => DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.PrepareDelta(
            StringState(DictionaryComparerKind.StringOrdinal, 1), StringState(DictionaryComparerKind.StringOrdinalIgnoreCase, 1), Strings));
    }

    [Fact]
    public void SameContentDifferentStringKeyIdentityUsesRemoveAndAdd() {
        var delta = DictionaryStateBody<ObjectId, int, StringIdStateOps, Int32StateOps>.PrepareDelta(
            StringState(DictionaryComparerKind.StringOrdinal, 1), StringState(DictionaryComparerKind.StringOrdinal, 2), Strings);
        Assert.Equal(new byte[] { 1, 1, 0, 1, 2, 0 }, delta.Body.ToArray());
    }

    private static FrozenDictionaryState<ObjectId, int> StringState(DictionaryComparerKind kind, params uint[] ids) =>
        new(kind, ids.Select((id, index) => new DictionaryEntryState<ObjectId, int>(new(id), index)).ToArray());

    private sealed class HostileComparer : IEqualityComparer<string> {
        public bool Equals(string? x, string? y) => throw new InvalidOperationException("Must not call custom comparer.");
        public int GetHashCode(string obj) => throw new InvalidOperationException("Must not call custom comparer.");
        public override bool Equals(object? obj) => throw new InvalidOperationException("Must not compare comparer instances through Equals.");
        public override int GetHashCode() => throw new InvalidOperationException("Must not hash custom comparer.");
    }
    private sealed class KeyNode : DurableBase { }
    private readonly record struct OrdinaryStruct(int Value);
    private enum CurrentEnum { Zero }
}
