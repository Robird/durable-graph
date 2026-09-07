using System.Reflection;
using System.Text;
using Atelia.DurableGraph;
using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void SchemaOnlyAllowsAbstractBasesAndDeclaringLayerFieldIds() {
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1));
        AssertSchemaOnlyCompiles(run);
        string generated = GeneratedSource(run, "DurableSchemas.g.cs");
        Assert.DoesNotContain("Serializer", generated);
        Assert.DoesNotContain("Upgrade", generated);
        Assert.DoesNotContain("Snapshot", generated);
        Assert.DoesNotContain(run.GeneratedSources, source => source.HintName == "DurableSnapshots.g.cs");

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type leaf = assembly.GetType("SchemaOnly.Leaf")!;
        DurableSchema schema = ReadSchemaOnly(leaf, 1);
        Assert.Equal(1, Assert.Single(schema.Fields).FieldId);
        Assert.Equal(1, Assert.Single(schema.BaseSchema!.Fields).FieldId);
        Assert.Equal(1, Assert.Single(schema.BaseSchema.BaseSchema!.Fields).FieldId);
        Assert.Null(schema.BaseSchema.BaseSchema.BaseSchema);
        Assert.Same(schema, ReadSchemaOnly(leaf, 1));
        Assert.Same(schema, leaf.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        foreach (int invalidVersion in new[] { int.MinValue, 0, 2, int.MaxValue }) {
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => ReadSchemaOnly(leaf, invalidVersion));
            Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
        }
    }

    [Fact]
    public void SchemaOnlyRequiresExplicitVersionBumpsAlongTheWholeChain() {
        AdditionalText[] history = SchemaOnlyV1History();
        GeneratorTestRun middleMismatch = RunGenerator(SchemaOnlyChain(2, 1, 1), history);
        Diagnostic middle = Assert.Single(middleMismatch.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0015");
        Assert.Contains("Middle", middle.GetMessage());
        GeneratorTestRun leafMismatch = RunGenerator(SchemaOnlyChain(2, 2, 1), history);
        Diagnostic leaf = Assert.Single(leafMismatch.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0015");
        Assert.Contains("Leaf", leaf.GetMessage());

        GeneratorTestRun updated = RunGenerator(SchemaOnlyChain(2, 2, 2), history);
        AssertSchemaOnlyCompiles(updated);
        Type leafType = EmitAndLoad(updated.OutputCompilation).GetType("SchemaOnly.Leaf")!;
        DurableSchema oldLeaf = ReadSchemaOnly(leafType, 1);
        DurableSchema newLeaf = ReadSchemaOnly(leafType, 2);
        Assert.Equal(1, oldLeaf.BaseSchema!.Version);
        Assert.Equal(1, oldLeaf.BaseSchema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Int32, Assert.Single(oldLeaf.BaseSchema.BaseSchema.Fields).TypeTag);
        Assert.Equal(2, newLeaf.BaseSchema!.Version);
        Assert.Equal(2, newLeaf.BaseSchema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Int64, Assert.Single(newLeaf.BaseSchema.BaseSchema.Fields).TypeTag);
    }

    [Fact]
    public void SchemaOnlyHistoryCannotUseCurrentCandidateToFillAnAncestorHole() {
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1),
            SchemaOnlyHistory("middle", 1, "base", 1),
            SchemaOnlyHistory("leaf", 1, "middle", 1));
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0019" && diagnostic.GetMessage().Contains("missing exact base"));
        Assert.DoesNotContain(run.GeneratedSources, source => source.HintName == "DurableSchemas.g.cs");
    }

    [Theory]
    [InlineData("// base:YmFzZQ==|01")]
    [InlineData("// base:YmFzZQ==|0")]
    [InlineData("// base:YmFzZQ==|-1")]
    [InlineData("// base:YmFzZQ==|2147483648")]
    [InlineData("// base:YmFzZQ==|1|2")]
    [InlineData("// base: YmFzZQ==|1")]
    [InlineData("// base:/w==|1")]
    [InlineData("// base:|1")]
    public void SchemaOnlyRejectsMalformedBaseRecord(string baseRecord) {
        string content = SnapshotHistory("bad.dgsnapshot", "base", 1).GetText()!.ToString()
            .Replace("// version:1\n", "// version:1\n" + baseRecord + "\n");
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1), new InMemoryAdditionalText("bad.dgsnapshot", content));
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0012");
    }

    [Theory]
    [InlineData("// base:YmFzZQ==|1\n// base:YmFzZQ==|1\n// field:1|2\n")]
    [InlineData("// field:1|2\n// base:YmFzZQ==|1\n")]
    public void SchemaOnlyRejectsDuplicateOrMisplacedBaseRecord(string records) {
        string content = SnapshotHistory("bad.dgsnapshot", "leaf", 1).GetText()!.ToString()
            .Replace("// snapshot-end", records + "// snapshot-end");
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1), new InMemoryAdditionalText("bad.dgsnapshot", content));
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0012");
    }

    [Fact]
    public void SchemaOnlyRejectsHistoryAncestryCyclesAndRepeatedIdsAcrossVersions() {
        foreach (AdditionalText[] history in new[] {
            new[] { SchemaOnlyHistory("a", 1, "b", 1), SchemaOnlyHistory("b", 1, "a", 1) },
            new[] { SchemaOnlyHistory("a", 2, "b", 1), SchemaOnlyHistory("b", 1, "a", 1), SchemaOnlyHistory("a", 1) },
        }) {
            GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1), history);
            Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0019" && diagnostic.GetMessage().Contains("repeats"));
        }
    }

    [Fact]
    public void SchemaOnlyDetectsConflictsWhereOnlyTheExactBaseDiffers() {
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(1, 1, 1),
            SchemaOnlyHistory("base", 1), SchemaOnlyHistory("base", 2),
            SchemaOnlyHistory("middle", 1, "base", 1),
            SchemaOnlyHistory("middle", 1, "base", 2));
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0013");
    }

    [Theory]
    [InlineData("GetSchema", "public static int GetSchema(int version) => version;")]
    [InlineData("__DurableSchemaHistory", "private class __DurableSchemaHistory { }")]
    [InlineData("Schema", "public static int Schema => 1;")]
    public void SchemaOnlyRejectsGeneratedMemberCollisions(string memberName, string declaration) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            [DurableType("single", 1)]
            public partial class Single : DurableBase {
                {{declaration}}
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => IsError(diagnostic) && diagnostic.GetMessage().Contains(memberName));
    }

    [Fact]
    public void SchemaOnlyDoesNotReserveSerializerOrPayloadSnapshotNames() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("single", 1)]
            public partial class Single : DurableBase {
                public static int Serializer => 1;
                private class __DurableSerializer { }
                private struct __DurableSnapshotV1 { }
            }
            """);
        AssertSchemaOnlyCompiles(run);
    }

    [Theory]
    [InlineData("public partial class Base : DurableBase { }")]
    public void SchemaOnlyRejectsNonOptedInAncestors(string baseDeclaration) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            {{baseDeclaration}}
            [DurableType("child", 1)]
            public partial class Child : Base { }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0019");
    }

    [Fact]
    public void SchemaOnlyRequiresEveryIntermediateVersion() {
        GeneratorTestRun run = RunGenerator(SchemaOnlyChain(3, 1, 1), SchemaOnlyV1History());
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0014" && diagnostic.GetMessage().Contains("version 2"));
    }

    [Fact]
    public void SchemaOnlyVersionOnlyBaseChangeStillRequiresDerivedBump() {
        GeneratorTestRun run = RunGenerator(
            SchemaOnlyChain(2, 1, 1).Replace("private long _base", "private int _base"), SchemaOnlyV1History());
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0015" && diagnostic.GetMessage().Contains("Middle"));
    }

    [Fact]
    public void SchemaOnlyStillRejectsDuplicateFieldIdsWithinOneDeclaration() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("single", 1)]
            public partial class Single : DurableBase {
                [DurableField(1)] private int _first;
                [DurableField(1)] private long _second;
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0006");
    }

    [Theory]
    [InlineData("public partial class Single<T> : DurableBase { }")]
    [InlineData("public class Single : DurableBase { }")]
    [InlineData("public partial record Single : DurableBase;")]
    public void SchemaOnlyRetainsTheBoundedClrTypeShape(string declaration) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            [DurableType("single", 1)]
            {{declaration}}
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0001");
    }

    [Theory]
    [InlineData("[DurableField(1)] private decimal _unsupported;", "", "DG0007")]
    [InlineData("", "[DurableType(\"base\", 1)] public partial class Other : DurableBase { }", "DG0017")]
    public void SchemaOnlyRejectsDescendantsOfFilteredAncestorsWithoutGeneratorFailure(
        string baseFields, string otherDeclaration, string ancestorDiagnostic) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            [DurableType("base", 1)]
            public partial class Base : DurableBase { {{baseFields}} }
            [DurableType("child", 1)]
            public sealed partial class Child : Base { }
            {{otherDeclaration}}
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == ancestorDiagnostic);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0019");
        Assert.DoesNotContain(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.DoesNotContain(run.GeneratedSources, source => source.HintName == "DurableSchemas.g.cs");
    }

    private static void AssertSchemaOnlyCompiles(GeneratorTestRun run) {
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), diagnostic => diagnostic.Id is "CS0108" or "CS0109");
    }

    private static DurableSchema ReadSchemaOnly(Type type, int version) =>
        Assert.IsType<DurableSchema>(type.GetMethod("GetSchema", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.Invoke(null, [version]));

    private static string SchemaOnlyChain(int baseVersion, int middleVersion, int leafVersion) => $$"""
        using Atelia.DurableGraph;
        namespace SchemaOnly;
        [DurableType("base", {{baseVersion}})]
        public abstract partial class Base : DurableBase {
            [DurableField(1)] private {{(baseVersion == 1 ? "int" : "long")}} _base;
        }
        [DurableType("middle", {{middleVersion}})]
        public partial class Middle : Base { [DurableField(1)] private int _middle; }
        [DurableType("leaf", {{leafVersion}})]
        public sealed partial class Leaf : Middle { [DurableField(1)] private int _leaf; }
        """;

    private static AdditionalText[] SchemaOnlyV1History() => [
        SchemaOnlyHistory("base", 1),
        SchemaOnlyHistory("middle", 1, "base", 1),
        SchemaOnlyHistory("leaf", 1, "middle", 1),
    ];

    private static AdditionalText SchemaOnlyHistory(string schemaId, int version, string? baseId = null, int baseVersion = 0) {
        string path = $"{schemaId}-{version}-{baseVersion}.dgsnapshot";
        string content = SnapshotHistory(path, schemaId, version, (1, 2)).GetText()!.ToString();
        if (baseId is not null) {
            string versionLine = $"// version:{version}\n";
            content = content.Replace(versionLine, versionLine +
                $"// base:{Convert.ToBase64String(Encoding.UTF8.GetBytes(baseId))}|{baseVersion}\n");
        }

        return new InMemoryAdditionalText(path, content);
    }
}
