using Atelia.DurableGraph;

namespace Atelia.DurableGraph.Tests;

public sealed class InMemoryStateStoreTests {
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
    public void VersionMismatchFailsBeforeDeserialization() {
        InMemoryStateStore stateStore = new();
        TestSerializer version1 = new(Schema(version: 1));
        TestSerializer version2 = new(Schema(version: 2));
        stateStore.Save("root", new TestState(), version1);

        StateSchemaMismatchException exception = Assert.Throws<StateSchemaMismatchException>(
            () => stateStore.Load("root", version2));

        Assert.Equal(1, exception.StoredSchema.Version);
        Assert.Equal(2, exception.ExpectedSchema.Version);
        Assert.Equal(0, version2.DeserializeCalls);
    }

    [Fact]
    public void SameKeyShapeMismatchFailsBeforeDeserialization() {
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
        Assert.Equal(0, conflicting.DeserializeCalls);
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

    private static DurableSchema Schema(int version) {
        return new DurableSchema(
            "tests.state",
            version,
            new DurableFieldInfo(1, TypeTag.Int32),
            new DurableFieldInfo(2, TypeTag.String));
    }

    private sealed class TestState : DurableBase {
        public int Number;

        public string? Text;

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
            return LastSerializedFields;
        }

        public TestState Deserialize(IReadOnlyDictionary<int, object?> fields) {
            DeserializeCalls++;
            return new TestState {
                Number = (int)fields[1]!,
                Text = (string?)fields[2],
            };
        }
    }

    private sealed class TestSerializationException : Exception {
    }
}
