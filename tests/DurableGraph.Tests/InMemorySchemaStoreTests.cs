using Atelia.DurableGraph;

namespace Atelia.DurableGraph.Tests;

public sealed class InMemorySchemaStoreTests {
    [Fact]
    public void FirstRegistrationBecomesTheExactStoredSchema() {
        InMemorySchemaStore store = new();
        DurableSchema schema = Schema(version: 1, TypeTag.String);

        DurableSchema registered = store.Register(schema);

        Assert.Same(schema, registered);
        Assert.Same(schema, store.GetRequired("tests.example", version: 1));
    }

    [Fact]
    public void EquivalentRegistrationIsIdempotent() {
        InMemorySchemaStore store = new();
        DurableSchema first = new(
            "tests.example",
            1,
            new DurableFieldInfo(2, TypeTag.String),
            new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema equivalent = new(
            "tests.example",
            1,
            new DurableFieldInfo(1, TypeTag.Int32),
            new DurableFieldInfo(2, TypeTag.String));

        store.Register(first);
        DurableSchema registered = store.Register(equivalent);

        Assert.Same(first, registered);
        Assert.Same(first, store.GetRequired("tests.example", version: 1));
    }

    [Fact]
    public void SameKeyWithDifferentShapeFailsAndPreservesRegisteredSchema() {
        InMemorySchemaStore store = new();
        DurableSchema registered = Schema(version: 1, TypeTag.String);
        DurableSchema conflicting = Schema(version: 1, TypeTag.Int32);
        store.Register(registered);

        SchemaConflictException exception = Assert.Throws<SchemaConflictException>(
            () => store.Register(conflicting));

        Assert.Equal("tests.example", exception.SchemaId);
        Assert.Equal(1, exception.Version);
        Assert.Same(registered, exception.RegisteredSchema);
        Assert.Same(conflicting, exception.ConflictingSchema);
        Assert.Same(registered, store.GetRequired("tests.example", version: 1));
    }

    [Fact]
    public void DifferentSchemaVersionsCoexist() {
        InMemorySchemaStore store = new();
        DurableSchema version1 = Schema(version: 1, TypeTag.String);
        DurableSchema version2 = Schema(version: 2, TypeTag.Int32);

        store.Register(version1);
        store.Register(version2);

        Assert.Same(version1, store.GetRequired("tests.example", version: 1));
        Assert.Same(version2, store.GetRequired("tests.example", version: 2));
    }

    [Fact]
    public void SchemaIdentityIsOrdinalAndCaseSensitive() {
        InMemorySchemaStore store = new();
        DurableSchema lowerCase = Schema("tests.example", version: 1, TypeTag.String);
        DurableSchema upperCase = Schema("Tests.Example", version: 1, TypeTag.String);

        store.Register(lowerCase);
        store.Register(upperCase);

        Assert.Same(lowerCase, store.GetRequired("tests.example", version: 1));
        Assert.Same(upperCase, store.GetRequired("Tests.Example", version: 1));
    }

    [Fact]
    public void MissingExactVersionFailsClosed() {
        InMemorySchemaStore store = new();
        store.Register(Schema(version: 1, TypeTag.String));

        SchemaNotFoundException exception = Assert.Throws<SchemaNotFoundException>(
            () => store.GetRequired("tests.example", version: 2));

        Assert.Equal("tests.example", exception.SchemaId);
        Assert.Equal(2, exception.Version);
    }

    [Fact]
    public void NullSchemaCannotBeRegistered() {
        InMemorySchemaStore store = new();

        Assert.Throws<ArgumentNullException>(() => store.Register(null!));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("tests.example", 0)]
    [InlineData("tests.example", -1)]
    public void InvalidLookupKeyIsRejected(string? schemaId, int version) {
        InMemorySchemaStore store = new();

        Assert.ThrowsAny<ArgumentException>(
            () => store.GetRequired(schemaId!, version));
    }

    private static DurableSchema Schema(int version, TypeTag typeTag) {
        return Schema("tests.example", version, typeTag);
    }

    private static DurableSchema Schema(
        string schemaId,
        int version,
        TypeTag typeTag) {
        return new DurableSchema(
            schemaId,
            version,
            new DurableFieldInfo(1, typeTag));
    }
}
