using System.Reflection;
using Atelia.DurableGraph;
using Atelia.DurableGraph.Schema;
using Atelia.DurableGraph.Runtime;

namespace Atelia.DurableGraph.Tests;

public sealed class DurableMetadataContractTests {
    [Fact]
    public void DurableTypeCarriesStableSchemaIdentityAndVersion() {
        DurableTypeAttribute attribute = Assert.Single(
            typeof(ExampleDurable).GetCustomAttributes<DurableTypeAttribute>());

        Assert.Equal("tests.example", attribute.SchemaId);
        Assert.Equal(3, attribute.Version);
    }

    [Fact]
    public void EveryExampleFieldIsExplicitlyClassified() {
        FieldInfo[] fields = typeof(ExampleDurable).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Collection(
            fields.OrderBy(field => field.Name),
            transientField => {
                Assert.Equal(nameof(ExampleDurable.Cache), transientField.Name);
                Assert.Empty(transientField.GetCustomAttributes<DurableFieldAttribute>());
                Assert.Single(transientField.GetCustomAttributes<TransientAttribute>());
            },
            durableField => {
                Assert.Equal(nameof(ExampleDurable.DisplayName), durableField.Name);
                DurableFieldAttribute attribute = Assert.Single(
                    durableField.GetCustomAttributes<DurableFieldAttribute>());
                Assert.Equal(17, attribute.FieldId);
                Assert.Empty(durableField.GetCustomAttributes<TransientAttribute>());
            });
    }

    [Theory]
    [InlineData(typeof(DurableTypeAttribute), AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    [InlineData(typeof(DurableFieldAttribute), AttributeTargets.Field)]
    [InlineData(typeof(TransientAttribute), AttributeTargets.Field)]
    public void AttributesAreRestrictedToTheirDeclarationKinds(
        Type attributeType,
        AttributeTargets expectedTargets) {
        AttributeUsageAttribute usage = Assert.Single(
            attributeType.GetCustomAttributes<AttributeUsageAttribute>());

        Assert.Equal(expectedTargets, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DurableTypeRejectsMissingIdentity(string? schemaId) {
        Assert.ThrowsAny<ArgumentException>(
            () => new DurableTypeAttribute(schemaId: schemaId!, version: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DurableTypeRejectsNonPositiveVersion(int version) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DurableTypeAttribute("tests.example", version));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DurableFieldRejectsNonPositiveIdentity(int fieldId) {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DurableFieldAttribute(fieldId));
    }

    [DurableType("tests.example", version: 3)]
    private sealed class ExampleDurable : IDurableObject {
        [DurableField(17)]
        public string DisplayName = string.Empty;

        [Transient]
        public object Cache = new();
    }
}
