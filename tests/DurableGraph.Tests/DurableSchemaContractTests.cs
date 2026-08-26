using Atelia.DurableGraph;

namespace Atelia.DurableGraph.Tests;

public sealed class DurableSchemaContractTests {
    [Theory]
    [InlineData(TypeTag.Invalid, 0)]
    [InlineData(TypeTag.Boolean, 1)]
    [InlineData(TypeTag.Int32, 2)]
    [InlineData(TypeTag.Int64, 3)]
    [InlineData(TypeTag.String, 4)]
    public void TypeTagsHaveStableCodes(TypeTag typeTag, int code) {
        Assert.Equal(code, (int)typeTag);
    }

    [Fact]
    public void FieldInfoHasStructuralValueSemantics() {
        DurableFieldInfo left = new(7, TypeTag.String);
        DurableFieldInfo right = new(7, TypeTag.String);

        Assert.Equal(left, right);
        Assert.Equal(7, left.FieldId);
        Assert.Equal(TypeTag.String, left.TypeTag);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FieldInfoRejectsNonPositiveIdentity(int fieldId) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DurableFieldInfo(fieldId, TypeTag.String));
    }

    [Theory]
    [InlineData(TypeTag.Invalid)]
    [InlineData((TypeTag)999)]
    public void FieldInfoRejectsUnsupportedTypeTag(TypeTag typeTag) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DurableFieldInfo(1, typeTag));
    }

    [Fact]
    public void SchemaCanonicalizesFieldOrderAndComparesStructurally() {
        DurableSchema left = new(
            "tests.example",
            2,
            new DurableFieldInfo(9, TypeTag.String),
            new DurableFieldInfo(3, TypeTag.Int32));
        DurableSchema right = new(
            "tests.example",
            2,
            new DurableFieldInfo(3, TypeTag.Int32),
            new DurableFieldInfo(9, TypeTag.String));

        Assert.Equal([3, 9], left.Fields.Select(field => field.FieldId));
        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void SchemaDefensivelyCopiesFields() {
        DurableFieldInfo[] source = [new DurableFieldInfo(1, TypeTag.String)];
        DurableSchema schema = new("tests.example", 1, source);

        source[0] = new DurableFieldInfo(2, TypeTag.Int64);

        DurableFieldInfo field = Assert.Single(schema.Fields);
        Assert.Equal(new DurableFieldInfo(1, TypeTag.String), field);
    }

    [Fact]
    public void SameSchemaIdentityAndVersionCanStillHaveDifferentShape() {
        DurableSchema expected = new(
            "tests.example",
            1,
            new DurableFieldInfo(1, TypeTag.String));
        DurableSchema conflicting = new(
            "tests.example",
            1,
            new DurableFieldInfo(1, TypeTag.Int32));

        Assert.NotEqual(expected, conflicting);
        Assert.True(expected != conflicting);
    }

    [Fact]
    public void SchemaRejectsDuplicateFieldIdentity() {
        Assert.Throws<ArgumentException>(
            () => new DurableSchema(
                "tests.example",
                1,
                new DurableFieldInfo(7, TypeTag.String),
                new DurableFieldInfo(7, TypeTag.Int32)));
    }

    [Fact]
    public void SchemaRejectsDefaultFieldInfo() {
        Assert.Throws<ArgumentException>(
            () => new DurableSchema("tests.example", 1, default(DurableFieldInfo)));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("tests.example", 0)]
    [InlineData("tests.example", -1)]
    public void SchemaRejectsInvalidIdentityOrVersion(string? schemaId, int version) {
        Assert.ThrowsAny<ArgumentException>(
            () => new DurableSchema(schemaId!, version));
    }
}
