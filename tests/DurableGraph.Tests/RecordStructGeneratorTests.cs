using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Theory]
    [InlineData("using Transient = System.NonSerializedAttribute;", "Transient")]
    [InlineData("class DurableField { }", "DurableField(1)")]
    public void RecordStructIgnoredAttributeUsesCompilerSuffixAndAmbiguityRules(string extra, string attribute) {
        string source = "using Atelia.DurableGraph; " + extra;
        GeneratorTestRun run = RunGenerator(source +
            " [DurableType(\"x\",1)] public partial record struct X { [field: " + attribute + "] public int Computed => 1; }");
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0021");
    }

    [Fact]
    public void RecordStructBackingAccessorNameCannotCollideWithUserMember() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            [DurableType("x",1)] public readonly partial record struct X([field: DurableField(1)] int Number) {
                private static void __DurableReadonly_Family_78_1() { }
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
    }

    [Fact]
    public void RecordStructUnrelatedFieldAttributeAliasDoesNotBecomeTransient() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            using Transient = System.NonSerializedAttribute;
            [DurableType("record.value", 1)] public partial record struct Value(
                [field: DurableField(1), @Transient] int X);
            """);
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        _ = EmitAndLoad(run.OutputCompilation);
    }

    [Fact]
    public void RecordStructUnrelatedAttributeAliasIsNotReinterpretedAsStorageClassification() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            using Transient = System.ObsoleteAttribute;
            [DurableType("x", 1)] public partial record struct X {
                [field: @Transient] public int Computed => 1;
                [@Transient] public int OtherComputed => 2;
                [@Transient] public int Read() => 3;
            }
            """);
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        // C# may warn that the field target is ignored; it is an unrelated attribute, not lost durable storage.
        _ = EmitAndLoad(run.OutputCompilation);
    }

    [Fact]
    public void RecordStructTypeNameCannotCollideWithGeneratedHostMember() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            [DurableType("record.value", 1)] public partial record struct __DurableCapture(
                [field: DurableField(1)] int X);
            """);
        Assert.Contains(run.GeneratorDiagnostics, IsError);
    }

    [Theory]
    [InlineData("public partial record struct Value([field: DurableField(1)] int X);")]
    [InlineData("public readonly partial record struct Value([field: DurableField(1)] int X);")]
    [InlineData("public readonly partial record struct Value<T>([field: DurableField(1)] T X) where T : struct;")]
    [InlineData("public partial record struct Value { [DurableField(1)] private int _x; }")]
    [InlineData("public readonly partial record struct Value { [DurableField(1)] private readonly int _x; }")]
    [InlineData("public partial record struct Value { [field: DurableField(1)] public int X { get; set; } }")]
    [InlineData("public readonly partial record struct Value { [field: DurableField(1)] public int X { get; init; } }")]
    [InlineData("public partial record struct Value { [field: DurableField(1)] public int X { get => field; set => field = value; } }")]
    [InlineData("public partial record struct Value([field: DurableField(1)] int @event) { [DurableField(2)] private int _extra; [field: Transient] public int Scratch { get; set; } }")]
    [InlineData("public partial record struct Value([field: DurableField(1)] int X); public partial record struct Value { [DurableField(2)] private int _extra; }")]
    [InlineData("public partial record struct Value { [DurableField(1)] private int _x; public int Computed => throw new System.Exception(); }")]
    [InlineData("public partial record struct Value(int X) { [DurableField(1)] public int X = X; }")]
    [InlineData("public partial record struct Value(int X) { [field: DurableField(1)] public int X { get; set; } = X; }")]
    [InlineData("public partial record struct Value([field: Transient] int Scratch);")]
    public void RecordStructSupportedStorageShapesCompile(string declaration) {
        GeneratorTestRun run = RunGenerator("using Atelia.DurableGraph; [DurableType(\"record.value\", 1)] " + declaration);
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        _ = EmitAndLoad(run.OutputCompilation);
    }

    [Theory]
    [InlineData("public partial record struct Value(int X);", "DG0003")]
    [InlineData("public partial record struct Value { public int X { get; set; } }", "DG0003")]
    [InlineData("public partial record struct Value { public int X { get => field; set => field = value; } }", "DG0003")]
    [InlineData("public partial record struct Value { private int _x; }", "DG0003")]
    [InlineData("public partial record struct Value([field: DurableField(1), Transient] int X);", "DG0004")]
    [InlineData("public partial record struct Value([field: DurableField(0)] int X);", "DG0005")]
    [InlineData("public partial record struct Value([field: DurableField(1)] int X, [field: DurableField(1)] int Y);", "DG0006")]
    [InlineData("public partial record struct Value([field: DurableField(1)] int X) { [DurableField(1)] private int _other; }", "DG0006")]
    [InlineData("public partial record struct Value { [field: DurableField(1)] public static int X { get; set; } }", "DG0009")]
    [InlineData("public partial record struct Value { [field: Transient] public static int X { get; set; } }", "DG0009")]
    public void RecordStructStorageClassificationCannotLoseBackingFields(string declaration, string diagnosticId) {
        GeneratorTestRun run = RunGenerator("using Atelia.DurableGraph; [DurableType(\"record.value\", 1)] " + declaration);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == diagnosticId && IsError(diagnostic));
    }

    [Theory]
    [InlineData("[field: DurableField(1)] public int Computed => 42;")]
    [InlineData("[field: Transient] public int Computed => 42;")]
    [InlineData("[field: Persist(1)] public int Computed => 42;")]
    [InlineData("[field: global::Atelia.DurableGraph.TransientAttribute] public int Computed => 42;")]
    public void RecordStructIgnoredClassificationOnComputedPropertyIsGeneratorError(string member) {
        GeneratorTestRun run = RunGenerator("using Atelia.DurableGraph; using Persist = Atelia.DurableGraph.DurableFieldAttribute; " +
            "[DurableType(\"record.value\", 1)] public partial record struct Value { " + member + " }");
        Assert.Contains(run.GeneratorDiagnostics, IsError);
    }

    [Theory]
    [InlineData("DurableField(1)", "[DurableField(1)] public int X = X;")]
    [InlineData("Transient", "[Transient] public int X = X;")]
    [InlineData("Persist(1)", "[DurableField(1)] public int X = X;")]
    [InlineData("global::Atelia.DurableGraph.DurableFieldAttribute(1)", "[field: DurableField(1)] public int X { get; set; } = X;")]
    [InlineData("global::Atelia.DurableGraph.TransientAttribute", "[field: Transient] public int X { get; set; } = X;")]
    public void RecordStructIgnoredPositionalClassificationIsNotMovedOntoReplacement(string annotation, string replacement) {
        GeneratorTestRun run = RunGenerator("using Atelia.DurableGraph; using Persist = Atelia.DurableGraph.DurableFieldAttribute; " +
            "[DurableType(\"record.value\", 1)] public partial record struct Value([field: " + annotation + "] int X) { " + replacement + " }");
        // Every real field is correctly classified, so an error must account for the ignored parameter annotation.
        Assert.Contains(run.GeneratorDiagnostics, IsError);
    }

    [Theory]
    [InlineData("[DurableType(\"record.value\", 1)] public partial record class Value;")]
    [InlineData("[DurableType(\"record.value\", 1)] public record struct Value([field: DurableField(1)] int X);")]
    [InlineData("[DurableType(\"record.value\", 1)] public ref partial struct Value { }")]
    [InlineData("public class Outer { [DurableType(\"record.value\", 1)] public partial record struct Value; }")]
    [InlineData("[DurableType(\"record.value\", 1)] file partial record struct Value;")]
    public void RecordStructEnrollmentDoesNotWidenUnsupportedOrUnqualifiedTypeShapes(string declaration) {
        GeneratorTestRun run = RunGenerator("using Atelia.DurableGraph; " + declaration);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0001");
    }

    [Fact]
    public void UnmarkedRecordStructIsNotAutomaticallyEnrolled() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            public partial record struct Value(int X);
            [DurableType("record.world", 1)] public partial class World : IDurableObject {
                [DurableField(1)] public Value Value;
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0007");
    }

    [Fact]
    public void RecordStructFieldLikeEventIsNotSilentlyDiscarded() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            [DurableType("record.value", 1)] public partial record struct Value {
                public event System.Action? Changed;
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, IsError);
    }
}
