using System.Reflection;
using System.Text;
using Atelia.DurableGraph.Build;
using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void SchemaAncestrySurvivesGeneratePublishUpgradeAndReload() {
        using AncestryHistoryDirectory files = new();
        SchemaHistoryTool publisher = new();
        GeneratorTestRun initial = RunGenerator(AncestrySource(1, 1, 1));
        AssertCompilesAncestry(initial);
        string manifest = files.WriteManifest(initial);
        publisher.Publish(manifest, files.History);
        Dictionary<string, string> originalHistory = files.ReadContents();
        Assert.Equal(3, originalHistory.Count);

        AdditionalText[] accepted = files.ReadAdditionalTexts();
        GeneratorTestRun missingMiddleBump = RunGenerator(AncestrySource(2, 1, 1), accepted);
        Assert.Contains(missingMiddleBump.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0015");
        Assert.Throws<SchemaHistoryException>(() => publisher.Publish(files.WriteManifest(missingMiddleBump), files.History));
        GeneratorTestRun missingLeafBump = RunGenerator(AncestrySource(2, 2, 1), accepted);
        Assert.Contains(missingLeafBump.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0015");
        Assert.Throws<SchemaHistoryException>(() => publisher.Publish(files.WriteManifest(missingLeafBump), files.History));
        Assert.Equal(3, files.ReadContents().Count);

        GeneratorTestRun upgraded = RunGenerator(AncestrySource(2, 2, 2), accepted.Reverse().ToArray());
        AssertCompilesAncestry(upgraded);
        Assembly assembly = EmitAndLoad(upgraded.OutputCompilation);
        Type leaf = Assert.IsAssignableFrom<Type>(assembly.GetType("Ancestry.Leaf"));
        DurableSchema oldLeaf = GetAncestrySchema(leaf, 1);
        DurableSchema currentLeaf = GetAncestrySchema(leaf, 2);
        Assert.Equal(1, oldLeaf.Version);
        Assert.Equal(1, oldLeaf.BaseSchema!.Version);
        Assert.Equal(1, oldLeaf.BaseSchema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Int32, Assert.Single(oldLeaf.BaseSchema.BaseSchema.Fields).TypeTag);
        Assert.Equal(2, currentLeaf.Version);
        Assert.Equal(2, currentLeaf.BaseSchema!.Version);
        Assert.Equal(2, currentLeaf.BaseSchema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Int64, Assert.Single(currentLeaf.BaseSchema.BaseSchema.Fields).TypeTag);
        Assert.Same(currentLeaf, leaf.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.Same(oldLeaf, GetAncestrySchema(leaf, 1));
        Assert.Null(leaf.GetProperty("Serializer", BindingFlags.Public | BindingFlags.Static));

        manifest = files.WriteManifest(upgraded);
        publisher.Publish(manifest, files.History);
        publisher.Verify(manifest, files.History);
        Assert.Equal(6, files.ReadContents().Count);
        foreach ((string name, string contents) in originalHistory) {
            Assert.Equal(contents, File.ReadAllText(Path.Combine(files.History, name)));
        }

        GeneratorTestRun reloaded = RunGenerator(AncestrySource(2, 2, 2), files.ReadAdditionalTexts());
        AssertCompilesAncestry(reloaded);
        Assert.Equal(GeneratedSource(upgraded, "DurableGraphSchemaHistoryCandidates.g.cs"),
            GeneratedSource(reloaded, "DurableGraphSchemaHistoryCandidates.g.cs"));
        Type reloadedLeaf = EmitAndLoad(reloaded.OutputCompilation).GetType("Ancestry.Leaf")!;
        Assert.Equal(oldLeaf, GetAncestrySchema(reloadedLeaf, 1));
        Assert.Equal(currentLeaf, GetAncestrySchema(reloadedLeaf, 2));
    }

    [Fact]
    public void SchemaAncestryHistoryDoesNotRequireTheOldClrBaseToRemainInSource() {
        using AncestryHistoryDirectory files = new();
        SchemaHistoryTool publisher = new();
        GeneratorTestRun initial = RunGenerator(AncestrySource(1, 1, 1));
        AssertCompilesAncestry(initial);
        publisher.Publish(files.WriteManifest(initial), files.History);
        const string changedBaseSource = """
            using Atelia.DurableGraph;
            namespace Ancestry;
            [DurableType("ancestry.replacement", 1)]
            public abstract partial class Replacement : IDurableObject {
                [DurableField(1)] public long ReplacementValue;
            }
            [DurableType("ancestry.leaf", 2)]
            public sealed partial class Leaf : Replacement {
                [DurableField(1)] public bool LeafValue;
            }
            """;
        GeneratorTestRun changed = RunGenerator(changedBaseSource, files.ReadAdditionalTexts());
        AssertCompilesAncestry(changed);
        Type leaf = EmitAndLoad(changed.OutputCompilation).GetType("Ancestry.Leaf")!;
        Assert.Equal("ancestry.replacement", GetAncestrySchema(leaf, 2).BaseSchema!.SchemaId);
        DurableSchema oldLeaf = GetAncestrySchema(leaf, 1);
        Assert.Equal("ancestry.middle", oldLeaf.BaseSchema!.SchemaId);
        Assert.Equal("ancestry.base", oldLeaf.BaseSchema.BaseSchema!.SchemaId);
        Assert.Equal(1, oldLeaf.BaseSchema.BaseSchema.Version);
        publisher.Publish(files.WriteManifest(changed), files.History);
        publisher.Verify(files.WriteManifest(changed), files.History);
    }

    private static void AssertCompilesAncestry(GeneratorTestRun run) {
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);
    }

    private static DurableSchema GetAncestrySchema(Type type, int version) {
        MethodInfo method = type.GetMethod("GetSchema", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
        Assert.NotNull(method);
        return Assert.IsType<DurableSchema>(method.Invoke(null, [version]));
    }

    private static string AncestrySource(int baseVersion, int middleVersion, int leafVersion) => $$"""
        using Atelia.DurableGraph;
        namespace Ancestry;
        [DurableType("ancestry.leaf", {{leafVersion}})]
        public sealed partial class Leaf : Middle {
            [DurableField(1)] public bool LeafValue;
        }
        [DurableType("ancestry.middle", {{middleVersion}})]
        public abstract partial class Middle : Base {
            [DurableField(1)] public long MiddleValue;
        }
        [DurableType("ancestry.base", {{baseVersion}})]
        public abstract partial class Base : IDurableObject {
            [DurableField(1)] public {{(baseVersion == 1 ? "int" : "long")}} BaseValue;
        }
        """;

    private sealed class AncestryHistoryDirectory : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DurableGraphAncestry", Guid.NewGuid().ToString("N"));

        public AncestryHistoryDirectory() => Directory.CreateDirectory(_root);

        public string History => Path.Combine(_root, "history");

        public string WriteManifest(GeneratorTestRun run) {
            string path = Path.Combine(_root, "manifest.g.cs");
            File.WriteAllText(path, GeneratedSource(run, "DurableGraphSchemaHistoryCandidates.g.cs"), new UTF8Encoding(false));
            return path;
        }

        public AdditionalText[] ReadAdditionalTexts() => Directory.GetFiles(History, "*.dgschema")
            .Order(StringComparer.Ordinal)
            .Select(path => (AdditionalText)new InMemoryAdditionalText(path, File.ReadAllText(path)))
            .ToArray();

        public Dictionary<string, string> ReadContents() => Directory.GetFiles(History, "*.dgschema")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText, StringComparer.Ordinal);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
