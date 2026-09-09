using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class NominalReferenceSchemaWireTests {
    [Fact]
    public void ReferenceOperandHasIndependentGoldenAndDoesNotRequireTargetRegistration() {
        DurableSchema owner = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "B"));
        byte[] golden = Convert.FromHexString("0101020102034100010001010F02034200");
        Assert.Equal(golden, SchemaCatalogTestData.Write([owner]));
        Assert.Equal(owner, SchemaCatalogTestData.Read(golden)[new("A", 1)]);
        for (int length = 0; length < golden.Length; length++) {
            byte[] truncated = golden[..length];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(truncated, SchemaCatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {length}: {error}");
        }
    }

    [Theory]
    [InlineData("0101020102034100010001010F")] // Missing operand.
    [InlineData("0101020102034100010001010F00")] // Unknown expression.
    [InlineData("0101020102034100010001010F02032000")] // Blank nominal identity.
    [InlineData("0101020102034100010001010F0203420000")] // Extra operand/trailing byte.
    [InlineData("0101020102034100010001010F0102")] // A numeric primitive cannot be a reference constraint.
    [InlineData("0101020102034100010001010F0104")] // String has its own canonical slot tag.
    public void MalformedNominalOperandsFailClosed(string hex) {
        Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), SchemaCatalogTestData.Empty));
        Assert.True(error is InvalidDataException or EndOfStreamException, error?.ToString());
    }

    [Fact]
    public void ChangingOnlyNominalFamilyConflictsWithPersistedDefinition() {
        DurableSchema first = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "B"));
        DurableSchema changed = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "C"));
        var registered = SchemaCatalogTestData.Registered(first);
        Assert.NotEqual(first, changed);
        byte[] changedRow = Convert.FromHexString("0101030102034100010001010F02034300");
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(changedRow, registered));
        Assert.Same(first, Assert.Single(registered).Value.Schema);
    }

    [Fact]
    public void RuntimeOperandIsExclusiveAndRequiredAndSelfReferenceIsNotAnExactDependency() {
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.ObjectReference));
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.ObjectReference, " "));
        Assert.ThrowsAny<ArgumentException>(() => new DurableFieldInfo(1, TypeTag.UInt32, "A"));
        DurableSchema self = new("A", 1, new DurableFieldInfo(1, TypeTag.ObjectReference, "A"));
        Assert.Null(self.BaseSchema);
        Assert.Equal(self, SchemaCatalogTestData.Read(SchemaCatalogTestData.Write([self]))[new("A", 1)]);
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
