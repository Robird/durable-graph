namespace Atelia.DurableGraph.Tests;

public sealed class StoredGraphNormalizationProbeTests {
    [Fact]
    public void MixedVersionsNormalizeInIdOrderAndOnlyHistoricalRecordsRequireRewrite() {
        (NormalizedBaselineGraph first, string[] firstTrace) = LoadMixedVersions(
            [3, 1, 2]);
        (NormalizedBaselineGraph second, string[] secondTrace) = LoadMixedVersions(
            [2, 3, 1]);

        AssertBaseline(
            first,
            rootId: 1,
            (1, Snapshot(10, nextId: 2, aliasId: 3), false),
            (2, Snapshot(20), true),
            (3, Snapshot(0), true));
        AssertBaseline(
            second,
            rootId: 1,
            (1, Snapshot(10, nextId: 2, aliasId: 3), false),
            (2, Snapshot(20), true),
            (3, Snapshot(0), true));
        Assert.Equal(
            [
                "decode-v2:1",
                "decode-v1:2",
                "upgrade:2",
                "decode-v1:3",
                "upgrade:0",
            ],
            firstTrace);
        Assert.Equal(firstTrace, secondTrace);
    }

    [Fact]
    public void FullTableSchemaPreflightRejectsBeforeDecodeOrUpgrade() {
        ProbeStoredSchema identityMismatch = new(
            StoredGraphNormalizationProbe.SchemaId.ToUpperInvariant(),
            version: 2,
            StoredGraphNormalizationProbe.SchemaV2.Fields.ToArray());
        ProbeStoredSchema shapeMismatch = new(
            StoredGraphNormalizationProbe.SchemaId,
            version: 2,
            new ProbeStoredFieldInfo(1, ProbeStoredFieldKind.Int32),
            new ProbeStoredFieldInfo(2, ProbeStoredFieldKind.Reference),
            new ProbeStoredFieldInfo(3, ProbeStoredFieldKind.Int32));
        ProbeStoredSchema unknownVersion = new(
            StoredGraphNormalizationProbe.SchemaId,
            version: 3,
            StoredGraphNormalizationProbe.SchemaV2.Fields.ToArray());

        (Func<Action, StoredProbeRecord> InvalidRecord, Type ExpectedException)[] cases = [
            (
                hook => new StoredProbeRecordV2(identityMismatch, Snapshot(20), hook),
                typeof(InvalidStoredGraphImageException)),
            (
                hook => new StoredProbeRecordV2(shapeMismatch, Snapshot(20), hook),
                typeof(InvalidStoredGraphImageException)),
            (
                hook => new StoredProbeRecordV2(unknownVersion, Snapshot(20), hook),
                typeof(UnsupportedSchemaVersionException)),
            (
                hook => new StoredProbeRecordV2(
                    StoredGraphNormalizationProbe.SchemaV1,
                    Snapshot(20),
                    hook),
                typeof(InvalidStoredGraphImageException)),
        ];

        foreach ((
            Func<Action, StoredProbeRecord> invalidRecord,
            Type expectedException) in cases) {
            int decodeCalls = 0;
            int upgradeCalls = 0;
            StoredGraphImage image = new(
                Id(1),
                [
                    Entry(1, Current(10, decodeHook: () => decodeCalls++)),
                    Entry(2, invalidRecord(() => decodeCalls++)),
                ]);

            Exception exception = Assert.ThrowsAny<Exception>(() =>
                StoredGraphNormalizationProbe.LoadNormalized(
                    image,
                    (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) => {
                        upgradeCalls++;
                        newValue = Snapshot(oldValue.Value, oldValue.NextId?.Value);
                    }));

            Assert.Equal(expectedException, exception.GetType());
            Assert.Equal(0, decodeCalls);
            Assert.Equal(0, upgradeCalls);
            Assert.Equal(new[] { Id(1), Id(2) }, OrderedIds(image.Records.Keys));
        }
    }

    [Fact]
    public void UpgradeFailureDoesNotReturnPartialStateAndRetryRestartsFromInput() {
        int currentDecodeCalls = 0;
        int historicalDecodeCalls = 0;
        int upgradeCalls = 0;
        bool failUpgrade = true;
        StoredProbeRecord currentRecord = Current(
            10,
            nextId: 2,
            decodeHook: () => currentDecodeCalls++);
        StoredProbeRecord historicalRecord = Historical(
            20,
            decodeHook: () => historicalDecodeCalls++);
        StoredGraphImage image = new(
            Id(1),
            [Entry(2, historicalRecord), Entry(1, currentRecord)]);
        ProbeUpgradeV1ToV2 handler =
            (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) => {
                upgradeCalls++;
                if (failUpgrade) {
                    throw new TestUpgradeFailureException();
                }

                newValue = Snapshot(oldValue.Value, oldValue.NextId?.Value);
            };
        NormalizedBaselineGraph? result = null;

        DurableUpgradeException failure = Assert.Throws<DurableUpgradeException>(() =>
            result = StoredGraphNormalizationProbe.LoadNormalized(image, handler));

        Assert.Null(result);
        Assert.IsType<TestUpgradeFailureException>(failure.InnerException);
        Assert.Equal(StoredGraphNormalizationProbe.SchemaId, failure.SchemaId);
        Assert.Equal(1, failure.FromVersion);
        Assert.Equal(2, failure.ToVersion);
        Assert.Equal(1, currentDecodeCalls);
        Assert.Equal(1, historicalDecodeCalls);
        Assert.Equal(1, upgradeCalls);
        Assert.Same(currentRecord, image.Records[Id(1)]);
        Assert.Same(historicalRecord, image.Records[Id(2)]);

        failUpgrade = false;
        result = StoredGraphNormalizationProbe.LoadNormalized(image, handler);

        Assert.Equal(2, currentDecodeCalls);
        Assert.Equal(2, historicalDecodeCalls);
        Assert.Equal(2, upgradeCalls);
        AssertBaseline(
            result,
            rootId: 1,
            (1, Snapshot(10, nextId: 2), false),
            (2, Snapshot(20), true));
    }

    [Fact]
    public void DecodeFailureDoesNotReturnPartialStateAndRetryRestartsFromInput() {
        int firstDecodeCalls = 0;
        int secondDecodeCalls = 0;
        bool failDecode = true;
        StoredProbeRecord firstRecord = Current(
            10,
            nextId: 2,
            decodeHook: () => firstDecodeCalls++);
        StoredProbeRecord secondRecord = Current(
            20,
            decodeHook: () => {
                secondDecodeCalls++;
                if (failDecode) {
                    throw new TestDecodeFailureException();
                }
            });
        StoredGraphImage image = new(
            Id(1),
            [Entry(2, secondRecord), Entry(1, firstRecord)]);
        NormalizedBaselineGraph? result = null;

        InvalidStoredGraphImageException failure =
            Assert.Throws<InvalidStoredGraphImageException>(() =>
                result = StoredGraphNormalizationProbe.LoadNormalized(
                    image,
                    upgradeHandler: null));

        Assert.Null(result);
        Assert.IsType<TestDecodeFailureException>(failure.InnerException);
        Assert.Equal(1, firstDecodeCalls);
        Assert.Equal(1, secondDecodeCalls);
        Assert.Same(firstRecord, image.Records[Id(1)]);
        Assert.Same(secondRecord, image.Records[Id(2)]);

        failDecode = false;
        result = StoredGraphNormalizationProbe.LoadNormalized(
            image,
            upgradeHandler: null);

        Assert.Equal(2, firstDecodeCalls);
        Assert.Equal(2, secondDecodeCalls);
        AssertBaseline(
            result,
            rootId: 1,
            (1, Snapshot(10, nextId: 2), false),
            (2, Snapshot(20), false));
    }

    [Fact]
    public void SourceExternalAndExplicitDefaultReferencesFailAcrossTheFullTable() {
        int currentUpgradeCalls = 0;
        StoredGraphImage currentExternal = ImageWithDisconnectedRecord(
            Current(20, nextId: 99));
        Assert.Throws<InvalidStoredGraphImageException>(() =>
            StoredGraphNormalizationProbe.LoadNormalized(
                currentExternal,
                (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) => {
                    currentUpgradeCalls++;
                    newValue = Snapshot(oldValue.Value);
                }));
        Assert.Equal(0, currentUpgradeCalls);

        int historicalUpgradeCalls = 0;
        StoredGraphImage upgradeExternal = ImageWithDisconnectedRecord(
            Historical(20));
        Assert.Throws<InvalidStoredGraphImageException>(() =>
            StoredGraphNormalizationProbe.LoadNormalized(
                upgradeExternal,
                (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) => {
                    historicalUpgradeCalls++;
                    newValue = Snapshot(oldValue.Value, nextId: 99);
                }));
        Assert.Equal(1, historicalUpgradeCalls);

        ProbeId? explicitDefaultId = default(ProbeId);
        StoredGraphImage defaultReference = ImageWithDisconnectedRecord(
            new StoredProbeRecordV2(
                StoredGraphNormalizationProbe.SchemaV2,
                new ProbeSnapshot(
                    Value: 20,
                    NextId: explicitDefaultId,
                    AliasId: null)));
        Assert.Throws<InvalidStoredGraphImageException>(() =>
            StoredGraphNormalizationProbe.LoadNormalized(
                defaultReference,
                upgradeHandler: null));
    }

    [Fact]
    public void UpgradeDeletedEdgePreservesSourceEntryAndFeedsR1PlanSave() {
        StoredGraphImage image = new(
            Id(1),
            [
                Entry(1, Historical(10, nextId: 2)),
                Entry(2, Current(20)),
            ]);
        NormalizedBaselineGraph baseline =
            StoredGraphNormalizationProbe.LoadNormalized(
                image,
                (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) =>
                    newValue = Snapshot(oldValue.Value));

        AssertBaseline(
            baseline,
            rootId: 1,
            (1, Snapshot(10), true),
            (2, Snapshot(20), false));

        ProbeNode currentRoot = new(Id(1), value: 10);
        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, currentRoot);

        Assert.Equal(Id(1), delta.ResultRootId);
        Assert.Equal(new[] { Id(1) }, OrderedIds(delta.Upserts.Keys));
        AssertSnapshot(Snapshot(10), delta.Upserts[Id(1)]);
        Assert.Equal(new[] { Id(2) }, OrderedIds(delta.UnreachableIds));
        Assert.True(baseline.Entries[Id(1)].RequiresRewrite);
    }

    [Fact]
    public void StoredGraphImageRejectsInvalidIdentityStructureBeforeDecode() {
        int decodeCalls = 0;
        Func<StoredProbeRecord> record = () =>
            Current(10, decodeHook: () => decodeCalls++);
        Action[] invalidConstructions = [
            () => _ = new StoredGraphImage(
                default,
                [Entry(1, record())]),
            () => _ = new StoredGraphImage(
                Id(2),
                [Entry(1, record())]),
            () => _ = new StoredGraphImage(
                Id(1),
                [Entry(1, record()), new StoredGraphRecordEntry(default, record())]),
            () => _ = new StoredGraphImage(
                Id(1),
                [Entry(1, record()), new StoredGraphRecordEntry(Id(2), null!)]),
            () => _ = new StoredGraphImage(
                Id(1),
                [Entry(1, record()), Entry(1, Current(99))]),
        ];

        foreach (Action invalidConstruction in invalidConstructions) {
            Assert.Throws<InvalidStoredGraphImageException>(invalidConstruction);
        }

        Assert.Equal(0, decodeCalls);
    }

    [Fact]
    public void StoredInputsAndNormalizedBaselineAreDetachedFromCallerMutation() {
        ProbeStoredFieldInfo[] schemaFields =
            StoredGraphNormalizationProbe.SchemaV2.Fields.ToArray();
        ProbeStoredSchema copiedSchema = new(
            StoredGraphNormalizationProbe.SchemaId,
            version: 2,
            schemaFields);
        ProbeSnapshot payload = Snapshot(10);
        StoredGraphRecordEntry[] entries = [
            Entry(1, new StoredProbeRecordV2(copiedSchema, payload)),
        ];
        StoredGraphImage image = new(Id(1), entries);

        schemaFields[0] = new ProbeStoredFieldInfo(
            99,
            ProbeStoredFieldKind.Reference);
        payload = Snapshot(99, nextId: 99);
        entries[0] = Entry(9, Current(99));

        NormalizedBaselineGraph baseline =
            StoredGraphNormalizationProbe.LoadNormalized(
                image,
                upgradeHandler: null);

        Assert.Equal(StoredGraphNormalizationProbe.SchemaV2, copiedSchema);
        Assert.Equal(new[] { Id(1) }, OrderedIds(image.Records.Keys));
        AssertBaseline(
            baseline,
            rootId: 1,
            (1, Snapshot(10), false));

        schemaFields[1] = new ProbeStoredFieldInfo(
            100,
            ProbeStoredFieldKind.Int32);
        entries[0] = Entry(10, Current(100));
        payload = Snapshot(100);

        AssertBaseline(
            baseline,
            rootId: 1,
            (1, Snapshot(10), false));
    }

    [Fact]
    public void CurrentOnlyAllowsNullHandlerButHistoricalRequiresOneBeforeDecode() {
        int currentDecodeCalls = 0;
        StoredGraphImage currentImage = new(
            Id(1),
            [Entry(1, Current(10, decodeHook: () => currentDecodeCalls++))]);

        NormalizedBaselineGraph currentBaseline =
            StoredGraphNormalizationProbe.LoadNormalized(
                currentImage,
                upgradeHandler: null);

        Assert.Equal(1, currentDecodeCalls);
        AssertBaseline(
            currentBaseline,
            rootId: 1,
            (1, Snapshot(10), false));

        int historicalDecodeCalls = 0;
        StoredGraphImage historicalImage = new(
            Id(1),
            [Entry(1, Historical(10, decodeHook: () => historicalDecodeCalls++))]);

        Assert.Throws<InvalidStoredGraphImageException>(() =>
            StoredGraphNormalizationProbe.LoadNormalized(
                historicalImage,
                upgradeHandler: null));
        Assert.Equal(0, historicalDecodeCalls);
    }

    private static (NormalizedBaselineGraph Baseline, string[] Trace) LoadMixedVersions(
        IReadOnlyList<long> inputOrder) {
        List<string> trace = [];
        Dictionary<long, StoredGraphRecordEntry> records = new() {
            [1] = Entry(
                1,
                Current(
                    10,
                    nextId: 2,
                    aliasId: 3,
                    decodeHook: () => trace.Add("decode-v2:1"))),
            [2] = Entry(
                2,
                Historical(
                    2,
                    decodeHook: () => trace.Add("decode-v1:2"))),
            [3] = Entry(
                3,
                Historical(
                    0,
                    decodeHook: () => trace.Add("decode-v1:3"))),
        };
        StoredGraphImage image = new(
            Id(1),
            inputOrder.Select(id => records[id]));

        NormalizedBaselineGraph baseline =
            StoredGraphNormalizationProbe.LoadNormalized(
                image,
                (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) => {
                    trace.Add($"upgrade:{oldValue.Value}");
                    newValue = Snapshot(
                        oldValue.Value * 10,
                        oldValue.NextId?.Value);
                });
        return (baseline, trace.ToArray());
    }

    private static StoredGraphImage ImageWithDisconnectedRecord(
        StoredProbeRecord disconnectedRecord) {
        return new StoredGraphImage(
            Id(1),
            [
                Entry(1, Current(10)),
                Entry(2, disconnectedRecord),
            ]);
    }

    private static StoredGraphRecordEntry Entry(
        long id,
        StoredProbeRecord record) {
        return new StoredGraphRecordEntry(Id(id), record);
    }

    private static StoredProbeRecord Historical(
        int value,
        long? nextId = null,
        Action? decodeHook = null) {
        return new StoredProbeRecordV1(
            StoredGraphNormalizationProbe.SchemaV1,
            new ProbeSnapshotV1(
                value,
                nextId is long next ? Id(next) : null),
            decodeHook);
    }

    private static StoredProbeRecord Current(
        int value,
        long? nextId = null,
        long? aliasId = null,
        Action? decodeHook = null) {
        return new StoredProbeRecordV2(
            StoredGraphNormalizationProbe.SchemaV2,
            Snapshot(value, nextId, aliasId),
            decodeHook);
    }

    private static ProbeId Id(long value) {
        return new ProbeId(value);
    }

    private static ProbeSnapshot Snapshot(
        int value,
        long? nextId = null,
        long? aliasId = null) {
        return new ProbeSnapshot(
            value,
            nextId is long next ? Id(next) : null,
            aliasId is long alias ? Id(alias) : null);
    }

    private static void AssertBaseline(
        NormalizedBaselineGraph actual,
        long rootId,
        params ExpectedBaselineEntry[] expectedEntries) {
        Assert.Equal(Id(rootId), actual.RootId);
        Assert.Equal(
            expectedEntries.Select(entry => Id(entry.Id)).OrderBy(id => id.Value),
            OrderedIds(actual.Entries.Keys));

        foreach (ExpectedBaselineEntry expected in expectedEntries) {
            Assert.True(
                actual.Entries.TryGetValue(Id(expected.Id), out BaselineEntry actualEntry));
            AssertSnapshot(expected.Snapshot, actualEntry.Snapshot);
            Assert.Equal(expected.RequiresRewrite, actualEntry.RequiresRewrite);
        }
    }

    private static void AssertSnapshot(
        ProbeSnapshot expected,
        ProbeSnapshot actual) {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.NextId, actual.NextId);
        Assert.Equal(expected.AliasId, actual.AliasId);
    }

    private static ProbeId[] OrderedIds(IEnumerable<ProbeId> ids) {
        return ids.OrderBy(id => id.Value).ToArray();
    }

    private readonly record struct ExpectedBaselineEntry(
        long Id,
        ProbeSnapshot Snapshot,
        bool RequiresRewrite) {
        public static implicit operator ExpectedBaselineEntry(
            (long Id, ProbeSnapshot Snapshot, bool RequiresRewrite) value) {
            return new ExpectedBaselineEntry(
                value.Id,
                value.Snapshot,
                value.RequiresRewrite);
        }
    }

    private sealed class TestUpgradeFailureException : Exception { }

    private sealed class TestDecodeFailureException : Exception { }
}
