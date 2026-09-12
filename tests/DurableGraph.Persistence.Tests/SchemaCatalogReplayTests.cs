using Atelia.DurableGraph.Schema;
using Atelia.Rbf;

namespace Atelia.DurableGraph.Persistence.Tests;

public sealed class SchemaCatalogReplayTests : IDisposable {
    private readonly List<string> _paths = [];
    private static readonly DurableSchema A = new("A", 1);
    private static readonly DurableSchema Point = new("P", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));

    [Fact]
    public void IndependentGoldenIncludesSchemaAndArrayRowsWithIntegerInlineDependency() {
        Assert.Equal(0x31424353U, SchemaCatalogWireCodec.RbfTag);
        byte[] golden = Convert.FromHexString("02020201020341000100000303010402");
        SchemaCatalogEntry[] rows = [SchemaCatalogEntry.ForSchema(new(2), A), Array(3, TypeTag.Int32)];
        Assert.Equal(golden, SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
        SchemaCatalogEntry[] read = SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty);
        Assert.Equal(rows.Select(static row => row.Id), read.Select(static row => row.Id));
        Assert.Equal(rows.Select(static row => row.Layout), read.Select(static row => row.Layout));

        byte[] inlineGolden = Convert.FromHexString("02020202020350000200010102030301041002");
        SchemaCatalogEntry[] inlineRows = [SchemaCatalogEntry.ForSchema(new(2), Point),
            SchemaCatalogEntry.ForArray(new(3), new(TypeExprKind.VectorArray, new(1, TypeTag.InlineValue, inlineSchema: Point)))];
        Assert.Equal(inlineGolden, SchemaCatalogWireCodec.Write(inlineRows, CatalogTestData.Empty));
        SchemaCatalogEntry[] decoded = SchemaCatalogWireCodec.Read(inlineGolden, CatalogTestData.Empty);
        Assert.Null(decoded[0].Layout);
        Assert.Equal(inlineRows[1].Layout, decoded[1].Layout);
        Assert.Same(decoded[0].Schema, decoded[1].Array!.ElementSlot.InlineSchema);
    }

    [Fact]
    public void AllBuiltinArraySlotsRanksAndNestedReferenceExpressionsHaveIndependentBytes() {
        for (byte constructor = 4; constructor <= 7; constructor++) {
            for (byte tag = 1; tag <= 14; tag++) {
                SchemaCatalogEntry row = SchemaCatalogEntry.ForArray(new(2), new((TypeExprKind)constructor, new(1, (TypeTag)tag)));
                byte[] golden = [2, 1, 2, 3, 1, constructor, tag];
                Assert.Equal(golden, SchemaCatalogWireCodec.Write([row], CatalogTestData.Empty));
                Assert.Equal(row.Layout, SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty)[0].Layout);
            }
        }
        SchemaCatalogEntry composed = SchemaCatalogEntry.ForArray(new(2), new(TypeExprKind.VectorArray, DurableFieldInfo.Reference(1,
            TypeExpr.VectorArray(TypeExpr.Named("B", TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.String), 4))))));
        byte[] composedGolden = Convert.FromHexString("0201020301040F0402034201070104");
        Assert.Equal(composedGolden, SchemaCatalogWireCodec.Write([composed], CatalogTestData.Empty));
        Assert.Equal(composed.Layout, SchemaCatalogWireCodec.Read(composedGolden, CatalogTestData.Empty)[0].Layout);
    }

    [Theory]
    [InlineData("03010203010402")] // Unknown batch version.
    [InlineData("0200")] // Empty physical batch.
    [InlineData("02FFFFFFFF0F")] // Count would exceed remaining bytes.
    [InlineData("02010003010402")] // ID zero.
    [InlineData("02010103010402")] // Reserved string ID.
    [InlineData("0201820003010402")] // Overlong ID.
    [InlineData("0201FFFFFFFF1003010402")] // ID overflow.
    [InlineData("02010200000000")] // Unknown node kind.
    [InlineData("02010203000402")] // Unknown array codec.
    [InlineData("02010203020402")] // Future array codec.
    [InlineData("0201020381000402")] // Noncanonical codec.
    [InlineData("02010203010302")] // Open constructor.
    [InlineData("02010203010802")] // Unsupported rank.
    [InlineData("02010203010400")] // Unsupported element.
    [InlineData("02010203010411")] // Open element.
    [InlineData("0201020301040F0104")] // String has its own canonical slot tag.
    [InlineData("0201020301040F040300")] // Open nested reference.
    [InlineData("0201020301041001")] // Builtin string is not an inline Schema.
    [InlineData("0201020301040200")] // Trailing byte.
    [InlineData("020202030104020203010405")] // Repeated ID.
    [InlineData("020203030104020203010405")] // Descending ID / initial gap.
    [InlineData("020202030104020303010402")] // Same layout under two IDs.
    public void MalformedBatchesFailClosed(string hex) {
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), CatalogTestData.Empty));
    }

    [Fact]
    public void EveryTruncatedPrefixFailsWithoutInstallingRows() {
        byte[] golden = Convert.FromHexString("02020202020350000200010102030301041002");
        Assert.Equal(2, SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty).Length);
        for (int length = 0; length < golden.Length; length++) {
            byte[] prefix = golden[..length];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(prefix, CatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Cut {length}: {error}");
            Assert.Empty(CatalogTestData.Empty);
        }
    }

    [Fact]
    public void WriterRejectsEmptyReservedUnorderedAndDuplicateNodes() {
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([], CatalogTestData.Empty));
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([Array(0, TypeTag.Int32)], CatalogTestData.Empty));
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([Array(1, TypeTag.Int32)], CatalogTestData.Empty));
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([Array(3, TypeTag.Int32), Array(2, TypeTag.Byte)], CatalogTestData.Empty));
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write([Array(2, TypeTag.Int32), Array(3, TypeTag.Int32)], CatalogTestData.Empty));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("rebound-id")]
    [InlineData("duplicate-layout")]
    [InlineData("gap")]
    [InlineData("tail")]
    [InlineData("meta")]
    public void InvalidRecoveredRowsAreNeverSkippedOrRenumbered(string kind) {
        string path = NewPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            new SchemaStore(file).RegisterRepresentations([Array(2, TypeTag.Int32).Layout!]);
            byte[] payload = kind switch {
                "duplicate-id" => CatalogTestData.Encode([Array(2, TypeTag.Int32)]),
                "rebound-id" => CatalogTestData.Encode([Array(2, TypeTag.Byte)]),
                "duplicate-layout" => CatalogTestData.Encode([Array(3, TypeTag.Int32)]),
                "gap" => CatalogTestData.Encode([Array(4, TypeTag.Byte)]),
                "tail" => [.. CatalogTestData.Encode([Array(3, TypeTag.Byte)]), 0],
                _ => CatalogTestData.Encode([Array(3, TypeTag.Byte)]),
            };
            file.Append(SchemaCatalogWireCodec.RbfTag, payload, kind == "meta" ? new byte[] { 1 } : []).Unwrap();
            file.DurableFlush();
        }
        byte[] original = File.ReadAllBytes(path);
        using (IRbfFile file = RbfFile.OpenExisting(path)) { Assert.Throws<InvalidDataException>(() => new SchemaStore(file)); }
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactDependencyCannotReferenceSchemaDeclaredLaterInTheLog(bool inline) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        DurableSchema dependency = inline ? Point : A;
        SchemaCatalogEntry future = SchemaCatalogEntry.ForSchema(new(3), dependency);
        SchemaCatalogEntry dependent = inline
            ? SchemaCatalogEntry.ForArray(new(2), new(TypeExprKind.VectorArray, new(1, TypeTag.InlineValue, inlineSchema: dependency)))
            : SchemaCatalogEntry.ForSchema(new(2), new("Child", 1, [], dependency));
        file.Append(SchemaCatalogWireCodec.RbfTag, CatalogTestData.Encode([dependent], CatalogTestData.Index([future]))).Unwrap();
        file.Append(SchemaCatalogWireCodec.RbfTag, CatalogTestData.Encode([future])).Unwrap();
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new SchemaStore(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRejectsSchemaAndArrayReferenceKindConflictRegardlessOfFrameOrder(bool schemaFirst) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        SchemaCatalogEntry schema = SchemaCatalogEntry.ForSchema(new(schemaFirst ? 2U : 3U), Point);
        SchemaCatalogEntry reference = SchemaCatalogEntry.ForArray(new(schemaFirst ? 3U : 2U), new(
            TypeExprKind.VectorArray, DurableFieldInfo.Reference(1, Point.Type)));
        SchemaCatalogEntry[] ordered = schemaFirst ? [schema, reference] : [reference, schema];
        foreach (SchemaCatalogEntry row in ordered) {
            file.Append(SchemaCatalogWireCodec.RbfTag, CatalogTestData.Encode([row])).Unwrap();
        }
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new SchemaStore(file));
    }

    private static SchemaCatalogEntry Array(uint id, TypeTag tag) =>
        SchemaCatalogEntry.ForArray(new(id), new(TypeExprKind.VectorArray, new(1, tag)));
    private string NewPath() {
        string path = Path.Combine(Path.GetTempPath(), $"durable-catalog-replay-{Guid.NewGuid():N}.rbf");
        _paths.Add(path);
        return path;
    }
    public void Dispose() { foreach (string path in _paths) { File.Delete(path); } }
}
