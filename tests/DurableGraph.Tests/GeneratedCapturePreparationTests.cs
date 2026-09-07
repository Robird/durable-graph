using System.Reflection;
using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedCapturePreparationBindsHeterogeneousFrozenObjectsAndStringChanges() {
        GeneratorTestRun run = RunGenerator(FusedDeltaPreamble + """
            using System.Linq;
            [DurableType("prepare.base", 1)]
            public abstract partial class Base : DurableBase {
                [DurableField(7)] private int _count = -17;
            }
            [DurableType("prepare.leaf", 1)]
            public sealed partial class Leaf : Base {
                [DurableField(1)] private uint _score = 42;
                [DurableField(2)] private string _name;
                public Leaf(string name) { _name = name; }
                public void Change(uint score, string name) { _score = score; _name = name; }
            }
            [DurableType("prepare.label", 1)]
            public sealed partial class Label : DurableBase {
                [DurableField(1)] private string _name;
                [DurableField(2)] private string _empty = string.Empty;
                public Label(string name) { _name = name; }
            }
            public static class Host {
                public static PreparedCapturedGraph[] Run() {
                    var session = new CaptureSession();
                    string shared = new string(new[] { 'A' });
                    var leaf = new Leaf(shared);
                    var label = new Label(shared);
                    using var first = session.BeginCapture();
                    Leaf.__DurableBinaryBody.AddRoot(first, leaf);
                    Label.__DurableBinaryBody.AddRoot(first, label);
                    Leaf.__DurableBinaryBody.AddRoot(first, leaf);
                    Label.__DurableBinaryBody.AddRoot(first, null);
                    var graph = first.Seal();
                    leaf.Change(43, new string(new[] { 'A' }));
                    var initial = session.Prepare(graph);
                    var repeated = session.Prepare(graph);
                    session.Accept(graph);
                    using var second = session.BeginCapture();
                    Leaf.__DurableBinaryBody.AddRoot(second, leaf);
                    Label.__DurableBinaryBody.AddRoot(second, label);
                    var changedGraph = second.Seal();
                    var changed = session.Prepare(changedGraph);
                    session.Accept(changedGraph);
                    using var third = session.BeginCapture();
                    Leaf.__DurableBinaryBody.AddRoot(third, leaf);
                    Label.__DurableBinaryBody.AddRoot(third, label);
                    var unchanged = session.Prepare(third.Seal());
                    return new[] { initial, repeated, changed, unchanged };
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        PreparedCapturedGraph[] results = assembly.GetType("FusedDelta.Host")!.GetMethod("Run")!
            .CreateDelegate<Func<PreparedCapturedGraph[]>>()();
        Assert.Equal(4, results.Length);
        var initial = results[0];
        Assert.Null(initial.Previous);
        Assert.Equal<uint>([1, 2, 1, 0], initial.Candidate.RootIds);
        Assert.Equal<uint>([1, 2, 3, 4], initial.Objects.Select(row => row.Current.Id));
        Assert.Equal(new[] { "212A03", "0304", "0341", "00" },
            initial.Objects.Select(row => Convert.ToHexString(row.BaseContent.Payload)));
        Assert.All(initial.Objects, row => { Assert.Null(row.Previous); Assert.Null(row.DeltaContent); });
        Assert.Same(initial.Candidate, results[1].Candidate);
        Assert.Equal(initial.Objects.Select(row => row.BaseContent.Payload.ToArray()),
            results[1].Objects.Select(row => row.BaseContent.Payload.ToArray()));

        var changed = results[2];
        Assert.Same(initial.Candidate, changed.Previous);
        Assert.Equal<uint>([1, 2, 3, 4, 5], changed.Objects.Select(row => row.Current.Id));
        Assert.Equal(new[] { "212B05", "0304", "0341", "00", "0341" },
            changed.Objects.Select(row => Convert.ToHexString(row.BaseContent.Payload)));
        Assert.Equal<byte>([0x06, 0x2B, 0x05], changed.Objects[0].DeltaContent!.Payload.ToArray());
        Assert.True(changed.Objects[0].DeltaContent!.HasChanges);
        Assert.False(changed.Objects[1].DeltaContent!.HasChanges);
        Assert.Equal<byte>([0], changed.Objects[1].DeltaContent!.Payload.ToArray());
        Assert.Same(initial.Candidate.Objects[0], changed.Objects[0].Previous);
        Assert.Null(changed.Objects[4].Previous);
        Assert.Null(changed.Objects[4].DeltaContent);
        Assert.NotSame(changed.Objects[2].Current.StringContent, changed.Objects[4].Current.StringContent);
        Assert.Same(string.Empty, changed.Objects[3].Current.StringContent);
        Assert.All(results[3].Objects.Where(row => row.Current.Kind == CapturedObjectKind.Durable), row => {
            Assert.False(row.DeltaContent!.HasChanges);
            Assert.Equal<byte>([0], row.DeltaContent.Payload.ToArray());
        });
        Assert.All(results[3].Objects.Where(row => row.Current.Kind == CapturedObjectKind.String), row => {
            Assert.NotNull(row.Previous);
            Assert.Null(row.DeltaContent);
        });

        string generated = GeneratedSource(run, "DurableBinaryBodies.g.cs");
        Assert.Contains("CapturedStatePreparation<V1> Preparation = new(V1.Schema, PrepareBase, PrepareDelta);", generated);
        Assert.Contains(".Schema, CaptureDelegate, Preparation);", generated);
        foreach (string forbidden in new[] { "ValueSlotCodec", "PrimitiveSlotCodecs", "DynamicInvoke", "System.Reflection", "Dictionary<" }) {
            Assert.DoesNotContain(forbidden, generated);
        }
        Type baseBody = assembly.GetType("FusedDelta.Base")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Assert.True(baseBody.GetField("Preparation", BindingFlags.NonPublic | BindingFlags.Static)!.IsInitOnly);
        Type leafBody = assembly.GetType("FusedDelta.Leaf")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        FieldInfo binding = leafBody.GetField("Preparation", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(binding.IsPrivate && binding.IsInitOnly);
        Assert.Same(binding.GetValue(null), binding.GetValue(null));
    }

    [Fact]
    public void GeneratedCapturePreparationBindsCurrentVersionWithoutChangingHistoricalBodyLayout() {
        using AncestryHistoryDirectory files = new();
        SnapshotHistoryTool publisher = new();
        const string initialSource = """
            using Atelia.DurableGraph;
            namespace PreparationHistory;
            [DurableType("prepare.history", 1)]
            public sealed partial class Item : DurableBase {
                [DurableField(1)] private int _value = -17;
            }
            """;
        GeneratorTestRun initial = RunGenerator(initialSource);
        AssertSchemaOnlyCompiles(initial);
        publisher.Publish(files.WriteManifest(initial), files.History);
        GeneratorTestRun current = RunGenerator("""
            using System;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace PreparationHistory;
            [DurableType("prepare.history", 2)]
            public sealed partial class Item : DurableBase {
                [DurableField(1)] private int _value = -17;
                [DurableField(2)] private byte _added = 99;
                public void Change() { _added = 100; }
            }
            public static class Host {
                public static PreparedCapturedGraph[] Run() {
                    var session = new CaptureSession();
                    var item = new Item();
                    using var first = session.BeginCapture();
                    Item.__DurableBinaryBody.AddRoot(first, item);
                    var graph = first.Seal();
                    var initial = session.Prepare(graph);
                    session.Accept(graph);
                    item.Change();
                    using var second = session.BeginCapture();
                    Item.__DurableBinaryBody.AddRoot(second, item);
                    return new[] { initial, session.Prepare(second.Seal()) };
                }
                public static byte[] Historical() {
                    var reader = new BinaryPayloadReader(new byte[] { 0x21 });
                    var state = Item.__DurableBinaryBody.ReadV1(ref reader);
                    reader.EnsureFullyConsumed();
                    return Item.__DurableBinaryBody.PrepareBase(in state).Payload.ToArray();
                }
            }
            """, files.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(current);
        Assembly assembly = EmitAndLoad(current.OutputCompilation);
        Type host = assembly.GetType("PreparationHistory.Host")!;
        var results = host.GetMethod("Run")!.CreateDelegate<Func<PreparedCapturedGraph[]>>()();
        Assert.Equal<byte>([0x21, 0x63], Assert.Single(results[0].Objects).BaseContent.Payload.ToArray());
        var updated = Assert.Single(results[1].Objects);
        Assert.Equal<byte>([0x21, 0x64], updated.BaseContent.Payload.ToArray());
        Assert.Equal<byte>([0x02, 0x64], updated.DeltaContent!.Payload.ToArray());
        Assert.Equal<byte>([0x21], host.GetMethod("Historical")!.CreateDelegate<Func<byte[]>>()());
        Type body = assembly.GetType("PreparationHistory.Item")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        FieldInfo binding = Assert.Single(body.GetFields(BindingFlags.Static | BindingFlags.NonPublic),
            field => field.Name == "Preparation");
        Assert.Equal(body.GetNestedType("V2", BindingFlags.NonPublic), Assert.Single(binding.FieldType.GenericTypeArguments));
        Assert.Single(body.GetNestedType("V1", BindingFlags.NonPublic)!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(2, body.GetNestedType("V2", BindingFlags.NonPublic)!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Length);
        Assert.Equal(2, body.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Count(method => method.Name == "PrepareBase"));
        Assert.Equal(2, body.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Count(method => method.Name == "PrepareDelta"));
    }
}
