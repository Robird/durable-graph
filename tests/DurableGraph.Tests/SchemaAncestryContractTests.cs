using Atelia.DurableGraph;

namespace Atelia.DurableGraph.Tests;

public sealed class SchemaAncestryContractTests {
    [Fact]
    public void ThreeLevelSchemaKeepsDeclaredFieldsAndExactAncestors() {
        DurableSchema baseSchema = Schema("base", 1);
        DurableSchema middle = Schema("middle", 1, baseSchema);
        DurableSchema leaf = Schema("leaf", 1, middle);

        Assert.Same(middle, leaf.BaseSchema);
        Assert.Same(baseSchema, leaf.BaseSchema!.BaseSchema);
        Assert.Null(baseSchema.BaseSchema);
        Assert.Equal(1, Assert.Single(leaf.Fields).FieldId);
        Assert.Equal(1, Assert.Single(middle.Fields).FieldId);
        Assert.Equal(1, Assert.Single(baseSchema.Fields).FieldId);
    }

    [Fact]
    public void EquivalentExactChainsHaveStructuralEqualityAndEqualHashes() {
        DurableSchema first = Schema("leaf", 1, Schema("middle", 1, Schema("base", 1)));
        DurableSchema equivalent = Schema("leaf", 1, Schema("middle", 1, Schema("base", 1)));

        Assert.NotSame(first.BaseSchema, equivalent.BaseSchema);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.True(first == equivalent);
    }

    [Fact]
    public void AncestorVersionAndShapeBothAffectDerivedEquality() {
        DurableSchema original = Schema("leaf", 1, Schema("middle", 1, Schema("base", 1)));
        DurableSchema changedVersion = Schema("leaf", 1, Schema("middle", 1, Schema("base", 2)));
        DurableSchema changedShape = Schema("leaf", 1, Schema("middle", 1,
            new DurableSchema("base", 1, new DurableFieldInfo(1, TypeTag.String))));

        Assert.NotEqual(original, changedVersion);
        Assert.NotEqual(original, changedShape);
        Assert.NotEqual(Schema("leaf", 1), original);
    }

    [Theory]
    [InlineData("base", 1)]
    [InlineData("base", 2)]
    [InlineData("middle", 2)]
    public void RepeatedAncestorIdentityIsRejectedEvenAcrossVersions(string schemaId, int version) {
        DurableSchema middle = Schema("middle", 1, Schema("base", 1));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Schema(schemaId, version, middle));

        Assert.Equal("baseSchema", exception.ParamName);
    }

    [Fact]
    public void AncestorIdentityUsesOrdinalComparison() {
        DurableSchema schema = Schema("Base", 1, Schema("base", 1));

        Assert.Equal("base", schema.BaseSchema!.SchemaId);
    }

    [Fact]
    public void InheritedSchemaStillCopiesAndCanonicalizesItsOwnFields() {
        DurableFieldInfo[] fields = [new(3, TypeTag.Int64), new(1, TypeTag.Int32)];
        DurableSchema schema = new("leaf", 1, fields, Schema("base", 1));
        fields[0] = new(9, TypeTag.String);

        Assert.Equal([1, 3], schema.Fields.Select(field => field.FieldId));
        Assert.Throws<ArgumentException>(() => new DurableSchema(
            "leaf", 1, [new(1, TypeTag.Int32), new(1, TypeTag.Int64)], schema.BaseSchema));
    }

    [Fact]
    public void DurableTypeHasNoGenerationModeFlags() {
        Assert.Null(typeof(DurableTypeAttribute).GetProperty("SchemaOnly"));
        Assert.Null(typeof(DurableTypeAttribute).GetProperty("GenerateBinaryBody"));
    }

    private static DurableSchema Schema(string schemaId, int version, DurableSchema? baseSchema = null) {
        return new DurableSchema(schemaId, version, [new DurableFieldInfo(1, TypeTag.Int32)], baseSchema);
    }
}
