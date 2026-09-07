using System.Reflection;
using Atelia.DurableGraph.Build;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedReferenceBodiesUseUInt32IdentityAndVisitEveryConstraint() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace ReferenceBodies;
            [DurableType("node", 1)]
            public partial class Node : DurableBase {
                [DurableField(1)] private Node? _next;
                [DurableField(2)] private string? _name;
                [DurableField(3)] private Node? _other;
            }
            public static class Host {
                public static PreparedBase Base() {
                    var state = new Node.__DurableBinaryBody.V1(128, 4, 0);
                    return Node.__DurableBinaryBody.PrepareBase(in state);
                }
                public static PreparedDelta Delta() {
                    var prior = new Node.__DurableBinaryBody.V1(128, 4, 0);
                    var current = new Node.__DurableBinaryBody.V1(128, 4, 129);
                    return Node.__DurableBinaryBody.PrepareDelta(in prior, in current);
                }
                public static byte[] Replay(byte[] delta) {
                    var prior = new Node.__DurableBinaryBody.V1(128, 4, 0);
                    var reader = new BinaryPayloadReader(delta);
                    var current = Node.__DurableBinaryBody.ApplyDeltaV1(ref reader, in prior);
                    return Node.__DurableBinaryBody.PrepareBase(in current).Payload.ToArray();
                }
                public static void Visit(IStateReferenceVisitor visitor) {
                    var state = new Node.__DurableBinaryBody.V1(128, 4, 0);
                    Node.__DurableBinaryBody.VisitReferences(in state, visitor);
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("ReferenceBodies.Host")!;
        Assert.Equal<byte>([0x80, 1, 4, 0], host.GetMethod("Base")!.CreateDelegate<Func<PreparedBase>>()().Payload.ToArray());
        PreparedDelta delta = host.GetMethod("Delta")!.CreateDelegate<Func<PreparedDelta>>()();
        Assert.True(delta.HasChanges);
        Assert.Equal<byte>([4, 0x81, 1], delta.Payload.ToArray());
        Assert.Equal<byte>([0x80, 1, 4, 0x81, 1],
            host.GetMethod("Replay")!.CreateDelegate<Func<byte[], byte[]>>()(delta.Payload.ToArray()));
        ReferenceBodyVisitor visitor = new();
        host.GetMethod("Visit")!.CreateDelegate<Action<IStateReferenceVisitor>>()(visitor);
        Assert.Equal(["durable:128:node", "string:4", "durable:0:node"], visitor.Entries);
        string generated = GeneratedSource(run, "DurableBinaryBodies.g.cs");
        AssertGeneratedBodiesRemainStaticallyBound(generated);
        Assert.Contains("writer.WriteUInt32(current.Segment0Field3)", generated);
        Assert.DoesNotContain("Node.__DurableBinaryBody.Model", generated);
    }

    [Fact]
    public void GeneratedReadonlyInheritedReferencesCaptureAndRestoreSharedMutualGraph() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            namespace ReferenceBodies;
            [DurableType("base", 1)]
            public abstract partial class Base : DurableBase {
                [DurableField(7)] private readonly Base? _parent;
                public Base? Parent => _parent;
                protected Base(Base? parent) { _parent = parent; }
            }
            [DurableType("node", 1)]
            public sealed partial class Node : Base {
                [DurableField(1)] private readonly Node? _self;
                [DurableField(2)] private Node? _child;
                [DurableField(3)] private string? _name;
                [Transient] public int Cache = 99;
                public static int Calls;
                public Node() : base(null) {
                    Calls++; _self = this; _name = "shared";
                    _child = new Node(this);
                }
                private Node(Node parent) : base(parent) { Calls++; _self = this; _name = parent._name; }
                public Node? Self => _self;
                public Node? Child => _child;
            }
            public static class Host {
                public static StateModelBinding[] Models() => [Node.__DurableBinaryBody.Model, Base.__DurableBinaryBody.Model];
                public static DurableBase New() => new Node();
                public static uint AddRoot(CaptureContext context, DurableBase root) => Node.__DurableBinaryBody.AddRoot(context, (Node)root);
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type host = assembly.GetType("ReferenceBodies.Host")!;
        StateModelBinding[] models = host.GetMethod("Models")!.CreateDelegate<Func<StateModelBinding[]>>()();
        DurableBase root = host.GetMethod("New")!.CreateDelegate<Func<DurableBase>>()();
        var addRoot = host.GetMethod("AddRoot")!.CreateDelegate<Func<CaptureContext, DurableBase, uint>>();
        CaptureSession session = new();
        using CaptureContext capture = session.BeginCapture(models);
        uint rootId = addRoot(capture, root);
        CapturedGraph graph = capture.Seal();
        Assert.Equal(3, graph.Objects.Count);
        Assert.Equal(rootId, Assert.Single(graph.RootIds));
        CapturedObject[] durable = graph.Objects.Where(item => item.Schema is not null).ToArray();
        Assert.Equal(2, durable.Length);
        StateModelBinding node = models[0];
        Dictionary<uint, DurableBase> instances = durable.ToDictionary(item => item.Id, _ => node.Allocate());
        ObjectReadTable objects = new(StringReadTable.FromDecoded(graph.Objects
            .Where(item => item.Schema is null).Select(item => (item.Id, item.StringContent))), instances);
        foreach (CapturedObject item in durable) node.Hydrate(instances[item.Id], item, objects);
        DurableBase restored = instances[rootId];
        Type type = restored.GetType();
        object child = type.GetProperty("Child")!.GetValue(restored)!;
        Assert.Same(restored, type.GetProperty("Self")!.GetValue(restored));
        Assert.Same(child, type.GetProperty("Self")!.GetValue(child));
        Assert.Same(restored, type.GetProperty("Parent")!.GetValue(child));
        Assert.Null(type.GetProperty("Parent")!.GetValue(restored));
        Assert.Equal(0, type.GetField("Cache")!.GetValue(restored));
        Assert.Equal(2, type.GetField("Calls")!.GetValue(null));
        Assert.NotSame(root, restored);
        AssertGeneratedBodiesRemainStaticallyBound(GeneratedSource(run, "DurableBinaryBodies.g.cs"));
    }

    [Fact]
    public void GeneratedHistoricalReferencesRetainNominalConstraintsAfterOldClrTypesAreRemoved() {
        using AncestryHistoryDirectory files = new();
        SchemaHistoryTool publisher = new();
        GeneratorTestRun initial = RunGenerator("""
            using Atelia.DurableGraph;
            namespace ReferenceBodies;
            [DurableType("retired", 1)]
            public partial class Retired : DurableBase { }
            [DurableType("owner", 1)]
            public partial class Owner : DurableBase { [DurableField(8)] private Retired? _reference; }
            """);
        AssertSchemaOnlyCompiles(initial);
        publisher.Publish(files.WriteManifest(initial), files.History);
        GeneratorTestRun current = RunGenerator("""
            using Atelia.DurableGraph;
            namespace ReferenceBodies;
            [DurableType("owner", 2)]
            public partial class Owner : DurableBase {
                private static void UpgradeStateV1ToV2(in __DurableBinaryBody.V1 prior, out __DurableBinaryBody.V2 next) => next = default;
            }
            public static class Host {
                public static StateModelBinding Model() => Owner.__DurableBinaryBody.Model;
                public static object Old() => new Owner.__DurableBinaryBody.V1(91);
            }
            """, files.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(current);
        Type host = EmitAndLoad(current.OutputCompilation).GetType("ReferenceBodies.Host")!;
        StateModelBinding model = host.GetMethod("Model")!.CreateDelegate<Func<StateModelBinding>>()();
        CapturedObject old = new(1, model.Readers[0].Schema, host.GetMethod("Old")!.CreateDelegate<Func<object>>()());
        ReferenceBodyVisitor visitor = new();
        model.Readers[0].VisitReferences(old, visitor);
        Assert.Equal(["durable:91:retired"], visitor.Entries);
        visitor.Entries.Clear();
        CapturedObject normalized = model.Normalize(old);
        model.VisitReferences(normalized, visitor);
        Assert.Empty(visitor.Entries);
        Assert.Equal(2, normalized.Schema!.Version);
        Assert.Equal("retired", model.Readers[0].Schema.Fields[0].TargetSchemaId);
        AssertGeneratedBodiesRemainStaticallyBound(GeneratedSource(current, "DurableBinaryBodies.g.cs"));
    }

    private sealed class ReferenceBodyVisitor : IStateReferenceVisitor {
        public List<string> Entries { get; } = [];
        public void VisitString(uint objectId) => Entries.Add($"string:{objectId}");
        public void VisitDurable(uint objectId, string nominalSchemaId) => Entries.Add($"durable:{objectId}:{nominalSchemaId}");
    }
}
