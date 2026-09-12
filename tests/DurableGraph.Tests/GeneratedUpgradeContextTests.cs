using Atelia.DurableGraph.Runtime;
using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedUpgradeContextBelongsToEachObjectAndAdjacentEdge() {
        using AncestryHistoryDirectory files = new();
        SchemaHistoryTool publisher = new();
        foreach (int version in new[] { 1, 2 }) {
            GeneratorTestRun previous = version == 1
                ? RunGenerator(StateModelHistorySource(version))
                : RunGenerator(StateModelHistorySource(version), files.ReadAdditionalTexts());
            AssertSchemaOnlyCompiles(previous);
            publisher.Publish(files.WriteManifest(previous), files.History);
        }
        GeneratorTestRun run = RunGenerator(StateModelHistorySource(3) + """
            public partial class Item {
                public static readonly System.Collections.Generic.List<UpgradeContext> Contexts = new();
                private static void UpgradeStateV1ToV2(in __DurableState.V1 prior, out __DurableState.V2 next, UpgradeContext context) {
                    Contexts.Add(context);
                    next = new(prior.Segment0Field1 + 1, 9);
                }
                private static void UpgradeStateV2ToV3(in __DurableState.V2 prior, out __DurableState.V3 next, UpgradeContext context) {
                    Contexts.Add(context);
                    next = new(prior.Segment0Field1, prior.Segment0Field2, true);
                }
            }
            public static class Host {
                public static StateModelBinding Model() => Item.__DurableState.Model;
                public static object Prior() => new Item.__DurableState.V1(5);
                public static UpgradeContext[] Contexts() => Item.Contexts.ToArray();
            }
            """, files.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("StateModels.Host")!;
        StateModelBinding model = host.GetMethod("Model")!.CreateDelegate<Func<StateModelBinding>>()();
        object prior = host.GetMethod("Prior")!.CreateDelegate<Func<object>>()();
        model.Normalize(new(new(31), model.Readers[0].Schema, prior));
        model.Normalize(new(new(42), model.Readers[0].Schema, prior));
        UpgradeContext[] contexts = host.GetMethod("Contexts")!.CreateDelegate<Func<UpgradeContext[]>>()();
        Assert.Equal<uint>([31, 31, 42, 42], contexts.Select(context => context.ObjectId.Value));
        Assert.Equal<int>([1, 2, 1, 2], contexts.Select(context => context.SourceObjectSchema.Version));
        Assert.Equal<int>([2, 3, 2, 3], contexts.Select(context => context.TargetObjectSchema.Version));
        Assert.Equal(4, contexts.Distinct(ReferenceEqualityComparer.Instance).Count());
        foreach (UpgradeContext context in contexts) {
            Assert.Equal(model.Readers[context.SourceObjectSchema.Version - 1].Schema, context.SourceObjectSchema);
            Assert.Equal(model.Readers[context.TargetObjectSchema.Version - 1].Schema, context.TargetObjectSchema);
        }
    }

    [Theory]
    [InlineData("string context")]
    [InlineData("ref UpgradeContext context")]
    [InlineData("in UpgradeContext context")]
    public void GeneratedUpgradeRejectsAnInvalidContextParameter(string parameter) {
        using AncestryHistoryDirectory files = new();
        GeneratorTestRun previous = RunGenerator(StateModelHistorySource(1));
        AssertSchemaOnlyCompiles(previous);
        new SchemaHistoryTool().Publish(files.WriteManifest(previous), files.History);
        GeneratorTestRun run = RunGenerator(StateModelHistorySource(2) + $$"""
            public partial class Item {
                private static void UpgradeStateV1ToV2(in __DurableState.V1 prior, out __DurableState.V2 next, {{parameter}}) {
                    next = default;
                }
            }
            """, files.ReadAdditionalTexts());
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
    }
}
