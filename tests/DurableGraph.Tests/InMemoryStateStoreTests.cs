using Atelia.DurableGraph;

namespace Atelia.DurableGraph.Tests;

public sealed class InMemoryStateStoreTests {
    [Fact]
    public void PublicSchemaExceptionsRejectNullSchemas() {
        DurableSchema schema = Schema(version: 1);

        Assert.Throws<ArgumentNullException>(
            () => new SchemaConflictException(null!, schema));
        Assert.Throws<ArgumentNullException>(
            () => new SchemaConflictException(schema, null!));
        Assert.Throws<ArgumentNullException>(
            () => new StateSchemaMismatchException(null!, schema));
        Assert.Throws<ArgumentNullException>(
            () => new StateSchemaMismatchException(schema, null!));
    }

    [Fact]
    public void SaveRegistersSchemaBeforeSerializingAndRoundTripsState() {
        InMemorySchemaStore schemaStore = new();
        InMemoryStateStore stateStore = new(schemaStore);
        TestSerializer serializer = new(Schema(version: 1));
        serializer.OnSerialize = () => Assert.Same(
            serializer.Schema,
            schemaStore.GetRequired("tests.state", version: 1));
        TestState source = new() {
            Number = 42,
            Text = "answer",
            Transient = 99,
        };

        stateStore.Save("root", source, serializer);
        TestState loaded = stateStore.Load("root", serializer);

        Assert.Equal(42, loaded.Number);
        Assert.Equal("answer", loaded.Text);
        Assert.Equal(0, loaded.Transient);
        Assert.Equal(1, serializer.SerializeCalls);
        Assert.Equal(1, serializer.DeserializeCalls);
        Assert.Same(serializer.Schema, serializer.LastDeserializedSchema);
    }

    [Fact]
    public void SchemaConflictFailsBeforeSerializationAndPreservesState() {
        InMemoryStateStore stateStore = new();
        TestSerializer original = new(Schema(version: 1));
        stateStore.Save(
            "root",
            new TestState { Number = 7, Text = "original" },
            original);
        TestSerializer conflicting = new(
            new DurableSchema(
                "tests.state",
                1,
                new DurableFieldInfo(1, TypeTag.String),
                new DurableFieldInfo(2, TypeTag.String)));

        Assert.Throws<SchemaConflictException>(
            () => stateStore.Save(
                "root",
                new TestState { Number = 9, Text = "replacement" },
                conflicting));

        Assert.Equal(0, conflicting.SerializeCalls);
        TestState loaded = stateStore.Load("root", original);
        Assert.Equal(7, loaded.Number);
        Assert.Equal("original", loaded.Text);
    }

    [Fact]
    public void SerializationFailureDoesNotReplaceExistingState() {
        InMemoryStateStore stateStore = new();
        TestSerializer serializer = new(Schema(version: 1));
        stateStore.Save(
            "root",
            new TestState { Number = 7, Text = "original" },
            serializer);
        serializer.ThrowOnSerialize = true;

        Assert.Throws<TestSerializationException>(
            () => stateStore.Save(
                "root",
                new TestState { Number = 9, Text = "replacement" },
                serializer));

        serializer.ThrowOnSerialize = false;
        TestState loaded = stateStore.Load("root", serializer);
        Assert.Equal(7, loaded.Number);
        Assert.Equal("original", loaded.Text);
    }

    [Fact]
    public void StoreDefensivelyCopiesSerializedFields() {
        InMemoryStateStore stateStore = new();
        TestSerializer serializer = new(Schema(version: 1));
        stateStore.Save(
            "root",
            new TestState { Number = 7, Text = "stored" },
            serializer);

        Assert.NotNull(serializer.LastSerializedFields);
        serializer.LastSerializedFields[1] = 99;
        serializer.LastSerializedFields[2] = "mutated";

        TestState loaded = stateStore.Load("root", serializer);
        Assert.Equal(7, loaded.Number);
        Assert.Equal("stored", loaded.Text);
    }

    [Fact]
    public void HistoricalLoadUpgradesInMemoryUntilExplicitSaveAdvancesState() {
        InMemorySchemaStore schemaStore = new();
        InMemoryStateStore stateStore = new(schemaStore);
        TestSerializer version1 = new(Schema(version: 1));
        VersionAwareSerializer version2 = new();
        stateStore.Save(
            "root",
            new TestState { Number = 7, Text = "old" },
            version1);

        Assert.Throws<SchemaNotFoundException>(
            () => schemaStore.GetRequired("tests.state", version: 2));

        TestState firstLoad = stateStore.Load("root", version2);
        TestState secondLoad = stateStore.Load("root", version2);

        Assert.Equal(7, firstLoad.Number);
        Assert.Equal("old", firstLoad.Text);
        Assert.True(firstLoad.Enabled);
        Assert.True(secondLoad.Enabled);
        Assert.Equal(2, version2.UpgradeCalls);
        Assert.Equal(0, version2.CurrentDeserializeCalls);
        Assert.Throws<SchemaNotFoundException>(
            () => schemaStore.GetRequired("tests.state", version: 2));

        stateStore.Save("root", firstLoad, version2);
        TestState currentLoad = stateStore.Load("root", version2);

        Assert.Same(
            version2.Schema,
            schemaStore.GetRequired("tests.state", version: 2));
        Assert.True(currentLoad.Enabled);
        Assert.Equal(2, version2.UpgradeCalls);
        Assert.Equal(1, version2.CurrentDeserializeCalls);
    }

    [Fact]
    public void SameKeyShapeMismatchIsDelegatedToSerializer() {
        InMemoryStateStore stateStore = new();
        TestSerializer stored = new(Schema(version: 1));
        stateStore.Save("root", new TestState(), stored);
        TestSerializer conflicting = new(
            new DurableSchema(
                "tests.state",
                1,
                new DurableFieldInfo(1, TypeTag.String),
                new DurableFieldInfo(2, TypeTag.String)));

        Assert.Throws<SchemaConflictException>(
            () => stateStore.Load("root", conflicting));
        Assert.Equal(1, conflicting.DeserializeCalls);
    }

    [Fact]
    public void DifferentSchemaIdentityFailsBeforeDeserialization() {
        InMemoryStateStore stateStore = new();
        TestSerializer stored = new(Schema(version: 1));
        stateStore.Save("root", new TestState(), stored);
        TestSerializer otherType = new(
            new DurableSchema(
                "tests.other-state",
                1,
                new DurableFieldInfo(1, TypeTag.Int32),
                new DurableFieldInfo(2, TypeTag.String)));

        Assert.Throws<StateSchemaMismatchException>(
            () => stateStore.Load("root", otherType));
        Assert.Equal(0, otherType.DeserializeCalls);
    }

    [Fact]
    public void FutureVersionProducesTypedFailureWithoutChangingStoredState() {
        InMemoryStateStore stateStore = new();
        TestSerializer version3 = new(Schema(version: 3));
        stateStore.Save(
            "root",
            new TestState { Number = 30, Text = "future", Enabled = true },
            version3);
        VersionAwareSerializer version2 = new();

        UnsupportedSchemaVersionException exception =
            Assert.Throws<UnsupportedSchemaVersionException>(
                () => stateStore.Load("root", version2));

        Assert.Equal("tests.state", exception.SchemaId);
        Assert.Equal(3, exception.StoredVersion);
        Assert.Equal(2, exception.CurrentVersion);

        TestState stillStored = stateStore.Load("root", version3);
        Assert.Equal(30, stillStored.Number);
        Assert.Equal("future", stillStored.Text);
        Assert.True(stillStored.Enabled);
    }

    [Fact]
    public void UpgradeFailureLeavesHistoricalStateAvailableForRetry() {
        InMemorySchemaStore schemaStore = new();
        InMemoryStateStore stateStore = new(schemaStore);
        TestSerializer version1 = new(Schema(version: 1));
        stateStore.Save(
            "root",
            new TestState { Number = 7, Text = "retry" },
            version1);
        VersionAwareSerializer version2 = new() { ThrowOnUpgrade = true };

        DurableUpgradeException exception = Assert.Throws<DurableUpgradeException>(
            () => stateStore.Load("root", version2));

        Assert.Equal("tests.state", exception.SchemaId);
        Assert.Equal(1, exception.FromVersion);
        Assert.Equal(2, exception.ToVersion);
        Assert.IsType<TestUpgradeException>(exception.InnerException);
        Assert.Throws<SchemaNotFoundException>(
            () => schemaStore.GetRequired("tests.state", version: 2));

        version2.ThrowOnUpgrade = false;
        TestState retried = stateStore.Load("root", version2);
        Assert.Equal(7, retried.Number);
        Assert.Equal("retry", retried.Text);
        Assert.True(retried.Enabled);

        TestState original = stateStore.Load("root", version1);
        Assert.False(original.Enabled);
    }

    private static DurableSchema Schema(int version) {
        if (version == 1) {
            return new DurableSchema(
                "tests.state",
                version,
                new DurableFieldInfo(1, TypeTag.Int32),
                new DurableFieldInfo(2, TypeTag.String));
        }

        return new DurableSchema(
            "tests.state",
            version,
            new DurableFieldInfo(1, TypeTag.Int32),
            new DurableFieldInfo(2, TypeTag.String),
            new DurableFieldInfo(3, TypeTag.Boolean));
    }

    private sealed class TestState : DurableBase {
        public int Number;

        public string? Text;

        public bool Enabled;

        public int Transient;
    }

    private sealed class TestSerializer : IDurableSerializer<TestState> {
        public TestSerializer(DurableSchema schema) {
            Schema = schema;
        }

        public DurableSchema Schema { get; }

        public int SerializeCalls { get; private set; }

        public int DeserializeCalls { get; private set; }

        public bool ThrowOnSerialize { get; set; }

        public Action? OnSerialize { get; set; }

        public Dictionary<int, object?>? LastSerializedFields { get; private set; }

        public DurableSchema? LastDeserializedSchema { get; private set; }

        public IReadOnlyDictionary<int, object?> Serialize(TestState value) {
            SerializeCalls++;
            OnSerialize?.Invoke();

            if (ThrowOnSerialize) {
                throw new TestSerializationException();
            }

            LastSerializedFields = new Dictionary<int, object?> {
                [1] = value.Number,
                [2] = value.Text,
            };

            if (Schema.Version >= 2) {
                LastSerializedFields[3] = value.Enabled;
            }

            return LastSerializedFields;
        }

        public TestState Deserialize(
            DurableSchema storedSchema,
            IReadOnlyDictionary<int, object?> fields) {
            DeserializeCalls++;

            LastDeserializedSchema = storedSchema;

            if (!storedSchema.Equals(Schema)) {
                if (storedSchema.Version == Schema.Version) {
                    throw new SchemaConflictException(storedSchema, Schema);
                }

                throw new UnsupportedSchemaVersionException(
                    Schema.SchemaId,
                    storedSchema.Version,
                    Schema.Version);
            }

            return new TestState {
                Number = (int)fields[1]!,
                Text = (string?)fields[2],
                Enabled = Schema.Version >= 2 && (bool)fields[3]!,
            };
        }
    }

    private sealed class VersionAwareSerializer : IDurableSerializer<TestState> {
        private static readonly DurableSchema Version1Schema = Schema(version: 1);

        public DurableSchema Schema { get; } = InMemoryStateStoreTests.Schema(
            version: 2);

        public int UpgradeCalls { get; private set; }

        public int CurrentDeserializeCalls { get; private set; }

        public bool ThrowOnUpgrade { get; set; }

        public IReadOnlyDictionary<int, object?> Serialize(TestState value) {
            return new Dictionary<int, object?> {
                [1] = value.Number,
                [2] = value.Text,
                [3] = value.Enabled,
            };
        }

        public TestState Deserialize(
            DurableSchema storedSchema,
            IReadOnlyDictionary<int, object?> fields) {
            switch (storedSchema.Version) {
                case 1:
                    if (!storedSchema.Equals(Version1Schema)) {
                        throw new SchemaConflictException(
                            Version1Schema,
                            storedSchema);
                    }

                    UpgradeCalls++;

                    try {
                        if (ThrowOnUpgrade) {
                            throw new TestUpgradeException();
                        }

                        return new TestState {
                            Number = (int)fields[1]!,
                            Text = (string?)fields[2],
                            Enabled = true,
                        };
                    } catch (Exception exception) {
                        throw new DurableUpgradeException(
                            Schema.SchemaId,
                            fromVersion: 1,
                            toVersion: 2,
                            exception);
                    }

                case 2:
                    if (!storedSchema.Equals(Schema)) {
                        throw new SchemaConflictException(Schema, storedSchema);
                    }

                    CurrentDeserializeCalls++;
                    return new TestState {
                        Number = (int)fields[1]!,
                        Text = (string?)fields[2],
                        Enabled = (bool)fields[3]!,
                    };

                default:
                    throw new UnsupportedSchemaVersionException(
                        Schema.SchemaId,
                        storedSchema.Version,
                        Schema.Version);
            }
        }
    }

    private sealed class TestSerializationException : Exception {
    }

    private sealed class TestUpgradeException : Exception {
    }
}
