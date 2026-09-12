namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void SourceMarkerLookalikeCannotGrantDurableQualification() {
        GeneratorTestRun run = RunGenerator("""
            namespace Atelia.DurableGraph { public interface IDurableObject { } }
            [Atelia.DurableGraph.DurableType("Counterfeit",1)]
            public partial class Counterfeit : Atelia.DurableGraph.IDurableObject {
                [Atelia.DurableGraph.DurableField(1)] public int Value;
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0001");
        Assert.DoesNotContain(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
    }

    [Theory]
    [InlineData("Bad")]
    [InlineData("System.Collections.Generic.List<Bad>")]
    [InlineData("Box<Bad>")]
    public void UnqualifiedSourceClassCannotEnterNominalOrGenericSlots(string fieldType) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            [DurableType("Bad",1)] public partial class Bad { [DurableField(1)] public int Value; }
            [DurableType("Box",1)] public partial class Box<T> : IDurableObject { [DurableField(1)] public T Value; }
            [DurableType("Root",1)] public partial class Root : IDurableObject { [DurableField(1)] public {{fieldType}} Value; }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0001");
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0007");
        Assert.DoesNotContain(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
    }
}
