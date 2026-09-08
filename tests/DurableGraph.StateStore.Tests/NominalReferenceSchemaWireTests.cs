using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class NominalReferenceSchemaWireTests {
    [Fact]
    public void ReferenceOperandHasIndependentGoldenAndDoesNotRequireTargetRegistration() {
        DurableSchema owner = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "B"));
        byte[] golden = Convert.FromHexString("04010203410001010001010F02034200");
        Assert.Equal(golden, SchemaBatchWireCodec.Write([owner]));
        Assert.Equal(owner, SchemaBatchWireCodec.Read(golden, new Dictionary<SchemaKey, DurableSchema>())[new("A", 1)]);
        for (int length = 0; length < golden.Length; length++) {
            byte[] truncated = golden[..length];
            Assert.ThrowsAny<Exception>(() => SchemaBatchWireCodec.Read(truncated, new Dictionary<SchemaKey, DurableSchema>()));
        }
    }

    [Theory]
    [InlineData("01010341010001010F")] // Missing operand.
    [InlineData("01010341010001010F00")] // Null/empty string representation.
    [InlineData("01010341010001010F0320")] // Blank nominal identity.
    [InlineData("01010341010001010F034200")] // Extra operand/trailing byte.
    [InlineData("010103410100010110")] // Inline tag 16 is illegal in v1.
    public void MalformedNominalOperandsFailClosed(string hex) {
        Assert.ThrowsAny<Exception>(() => SchemaBatchWireCodec.Read(Convert.FromHexString(hex), new Dictionary<SchemaKey, DurableSchema>()));
    }

    [Fact]
    public void ChangingOnlyNominalFamilyConflictsWithPersistedDefinition() {
        DurableSchema first = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "B"));
        DurableSchema changed = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "C"));
        var registered = new Dictionary<SchemaKey, DurableSchema> { [new("A", 1)] = first };
        Assert.NotEqual(first, changed);
        Assert.Throws<InvalidDataException>(() => SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([changed]), registered));
        Assert.Same(first, Assert.Single(registered).Value);
    }

    [Fact]
    public void RuntimeOperandIsExclusiveAndRequiredAndSelfReferenceIsNotAnExactDependency() {
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.ObjectReference));
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.ObjectReference, " "));
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.UInt32, "A"));
        DurableSchema self = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "A"));
        Assert.Null(self.BaseSchema);
        Assert.Equal(self, SchemaBatchWireCodec.Read(SchemaBatchWireCodec.Write([self]), new Dictionary<SchemaKey, DurableSchema>())[new("A", 1)]);
    }

    [Fact]
    public void SchemaStoreRegistersNominalCyclesWithoutTargetClosureAndColdConflictIsAtomic() {
        string path = Path.Combine(Path.GetTempPath(), "nominal-schema-" + Guid.NewGuid().ToString("N") + ".rbf");
        DurableSchema a = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "B"));
        DurableSchema b = new("B", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "A"));
        try {
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                SchemaStore store = new(file);
                store.Register(a);
                Assert.Equal(1, store.Count); // Nominal names do not require or register exact target definitions.
                store.Register(b);
            }
            using (IRbfFile file = RbfFile.OpenExisting(path)) {
                SchemaStore store = new(file);
                Assert.Equal(a, store.GetRequired("A", 1));
                Assert.Equal(b, store.GetRequired("B", 1));
                long before = file.TailOffset;
                Assert.Throws<SchemaConflictException>(() => store.RegisterBatch([
                    new("Independent", 1), new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "C"))]));
                Assert.Equal(before, file.TailOffset);
                Assert.Equal(2, store.Count);
                Assert.False(store.IsFaulted);
            }
        }
        finally { File.Delete(path); }
    }
}
