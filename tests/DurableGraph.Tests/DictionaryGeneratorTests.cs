using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void DictionaryCompositionsKeepReferenceSlotsAndBothNominalOperands() {
        GeneratorTestRun run = RunGenerator("""
            using System.Collections.Generic;
            using Atelia.DurableGraph;
            [DurableType("Key",1)] public enum Key : ushort { A=1 }
            [DurableType("Point",1)] public partial struct Point { [DurableField(1)] public int X; }
            [DurableType("Pair",1)] public partial struct Pair<T,U> {
                [DurableField(1)] public T First;
                [DurableField(2)] public U Second;
            }
            [DurableType("Recursive",1)] public partial struct Recursive {
                [DurableField(1)] public Dictionary<int,Recursive> Children;
            }
            [DurableType("Box",1)] public partial class Box<T>:IDurableObject { [DurableField(1)] public T Value; }
            [DurableType("Map",1)] public partial class Map<K,V>:IDurableObject where K:notnull {
                [DurableField(1)] public Dictionary<K,V> Items;
            }
            [DurableType("Derived",1)] public partial class Derived:Box<Dictionary<Key,Point?>> {}
            [DurableType("World",1)] public partial class World:IDurableObject {
                [DurableField(1)] public Dictionary<string,List<Dictionary<int,Point?>>> Nested;
                [DurableField(2)] public List<Dictionary<Key,Point[,]>> Arrays;
                [DurableField(3)] public Dictionary<int,World>[] Maps;
                [DurableField(4)] public Pair<Dictionary<int,Point>,Key> Pair;
                [DurableField(5)] public Dictionary<World,Recursive> References;
            }
            """);
        AssertSchemaOnlyCompiles(run);
        string generated = GeneratedSource(run, "DurableGenericStates.g.cs");
        Assert.Contains("global::Atelia.DurableGraph.TypeExpr.Dictionary(", generated);
        Assert.Contains("context.CaptureObject(field", generated);
        Assert.Contains("objects.ResolveObject<", generated);
        Assert.Contains("global::Atelia.DurableGraph.ObjectId", generated);
        string manifest = GeneratedSource(run, "DurableGraphSchemaHistoryCandidates.g.cs");
        Assert.Contains("manifest:9", manifest);
        Assert.Contains("// field:1|15|d(b4,l(d(b2,q(nUG9pbnQ=()))))", manifest);
        Assert.Contains("// field:2|15|l(d(nS2V5(),a2(nUG9pbnQ=())))", manifest);
        Assert.Contains("// field:3|15|a1(d(b2,nV29ybGQ=()))", manifest);
        Assert.Contains("// field:1|15|d(p0,p1)", manifest);
        Assert.Contains("// base:nQm94(d(nS2V5(),q(nUG9pbnQ=())))|1", manifest);
    }

    [Theory]
    [InlineData("Dictionary<UnregisteredKey,int>")]
    [InlineData("Dictionary<int?,int>")]
    [InlineData("List<Dictionary<UnregisteredKey,int>>")]
    [InlineData("Dictionary<int,Dictionary<UnregisteredKey,int>>")]
    [InlineData("Dictionary<Unmarked,int>")]
    [InlineData("Dictionary<object,int>")]
    [InlineData("Dictionary<int,object>")]
    [InlineData("IDictionary<int,int>")]
    [InlineData("SortedDictionary<int,int>")]
    [InlineData("ChildDictionary")]
    public void UnsupportedDictionaryKeysValuesAndContainerShapesAreRejected(string fieldType) {
        GeneratorTestRun run = RunGenerator("using System.Collections.Generic; using Atelia.DurableGraph; " +
            "[DurableType(\"Point\",1)] public partial struct Point { [DurableField(1)] public int X; } " +
            "public enum Unmarked {A} public struct UnregisteredKey {public int X;} public class ChildDictionary:Dictionary<int,int> {} " +
            "[DurableType(\"Bad\",1)] public partial class Bad:IDurableObject { " +
            "[DurableField(1)] public " + fieldType + " Value; }");
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0007");
    }

    [Fact]
    public void SourceDefinedFrameworkNamedDictionaryDoesNotBecomeBuiltin() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            namespace System.Collections.Generic { public class Dictionary<K,V> {} }
            [DurableType("Bad",1)] public partial class Bad:IDurableObject {
                [DurableField(1)] public System.Collections.Generic.Dictionary<int,int> Value;
            }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0007");
    }

    [Fact]
    public void DurableUserDictionaryRemainsNamedAndDoesNotInheritBuiltinKeyRules() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("Point",1)] public partial struct Point { [DurableField(1)] public int X; }
            [DurableType("UserMap",1)] public partial class Dictionary<K,V>:IDurableObject {
                [DurableField(1)] public K Key;
                [DurableField(2)] public V Value;
            }
            [DurableType("World",1)] public partial class World:IDurableObject {
                [DurableField(1)] public Dictionary<Point,int> User;
                [DurableField(2)] public System.Collections.Generic.Dictionary<int,Point> Builtin;
            }
            """);
        AssertSchemaOnlyCompiles(run);
        string manifest = GeneratedSource(run, "DurableGraphSchemaHistoryCandidates.g.cs");
        Assert.Contains("// field:1|15|nVXNlck1hcA==(nUG9pbnQ=(),b2)", manifest);
        Assert.Contains("// field:2|15|d(b2,nUG9pbnQ=())", manifest);
    }

    [Fact]
    public void DictionaryKeyAndValueInlineVersionsDoNotChangeReferencingOwner() {
        using AncestryHistoryDirectory history = new();
        const string source = """
            using System.Collections.Generic;
            using Atelia.DurableGraph;
            [DurableType("Key",1)] public enum Key : byte { A=1 }
            [DurableType("Point",1)] public partial struct Point { [DurableField(1)] public int X; }
            [DurableType("World",1)] public partial class World:IDurableObject {
                [DurableField(1)] public Dictionary<Key,Point> Items;
            }
            """;
        GeneratorTestRun first = RunGenerator(source);
        AssertSchemaOnlyCompiles(first);
        new SchemaHistoryTool().Publish(history.WriteManifest(first), history.History);
        GeneratorTestRun second = RunGenerator(source.Replace("\"Key\",1", "\"Key\",2").Replace("enum Key : byte", "enum Key : long")
            .Replace("\"Point\",1", "\"Point\",2").Replace("public int X;", "public long X;"), history.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(second);
        Assert.Contains("// field:1|15|d(nS2V5(),nUG9pbnQ=())", GeneratedSource(second, "DurableGraphSchemaHistoryCandidates.g.cs"));
        Assert.DoesNotContain(second.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0016");
    }

    [Fact]
    public void ClosedOwnerUpgradeRegistrationPreservesDictionaryOperands() {
        using AncestryHistoryDirectory history = new();
        GeneratorTestRun first = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("Box",1)] public partial class Box<T>:IDurableObject { [DurableField(1)] public T Value; }
            """);
        AssertSchemaOnlyCompiles(first);
        new SchemaHistoryTool().Publish(history.WriteManifest(first), history.History);
        GeneratorTestRun second = RunGenerator("""
            using System.Collections.Generic;
            using Atelia.DurableGraph;
            using States=Atelia.DurableGraph.Generated.Family_426F78;
            [DurableType("Box",2)] public partial class Box<T>:IDurableObject {
                [DurableField(1)] public T Value;
                [DurableField(2)] public int Count;
            }
            public static class Upgrades {
                [DurableUpgrade(typeof(Box<Dictionary<string,int?>>),1)]
                public static void Upgrade(in States.V1<ObjectId> prior,out States.V2<ObjectId> next,UpgradeContext context) =>
                    next=new(prior.Segment0Field1,0);
            }
            """, history.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(second);
        string generated = GeneratedSource(second, "DurableGenericStates.g.cs");
        Assert.Contains("closedOwner: global::Atelia.DurableGraph.TypeExpr.Named(\"Box\", global::Atelia.DurableGraph.TypeExpr.Dictionary(", generated);
        Assert.Contains("global::Atelia.DurableGraph.TypeExpr.Nullable(", generated);
    }
}
