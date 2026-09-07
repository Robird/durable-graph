using System.Reflection;
using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedReaderBindingsRegisterHistoricalAndCurrentExactSchemasWithStableIdentity() {
        using AncestryHistoryDirectory files = new();
        SchemaHistoryTool publisher = new();
        GeneratorTestRun initial = RunGenerator("""
            using Atelia.DurableGraph;
            namespace ReaderHistory;
            [DurableType("reader.base", 1)]
            public abstract partial class RemovedBase : DurableBase {
                [DurableField(9)] private int _old;
            }
            [DurableType("reader.leaf", 1)]
            public sealed partial class Leaf : RemovedBase {
                [DurableField(1)] private string? _name;
            }
            """);
        AssertSchemaOnlyCompiles(initial);
        publisher.Publish(files.WriteManifest(initial), files.History);
        const string currentSource = """
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace ReaderHistory;
            [DurableType("reader.base", 2)]
            public abstract partial class CurrentBase : DurableBase {
                [DurableField(2)] private byte _new;
            }
            [DurableType("reader.leaf", 2)]
            public sealed partial class Leaf : CurrentBase {
                [DurableField(1)] private uint _number;
            }
            public static class Host {
                public static void Register(IStateReaderRegistration readers) {
                    Leaf.__DurableBinaryBody.RegisterReaders(readers);
                }
                public static void RegisterBase(IStateReaderRegistration readers) {
                    CurrentBase.__DurableBinaryBody.RegisterReaders(readers);
                }
                public static byte[] Historical() {
                    var reader = new BinaryPayloadReader(new byte[] { 0x21, 0x03 });
                    var state = Leaf.__DurableBinaryBody.ReadV1(ref reader);
                    reader.EnsureFullyConsumed();
                    return Leaf.__DurableBinaryBody.PrepareBase(in state).Payload.ToArray();
                }
            }
            """;
        GeneratorTestRun current = RunGenerator(currentSource, files.ReadAdditionalTexts().Reverse().ToArray());
        AssertSchemaOnlyCompiles(current);
        Assembly assembly = EmitAndLoad(current.OutputCompilation);
        Assert.Null(assembly.GetType("ReaderHistory.RemovedBase"));
        Type host = assembly.GetType("ReaderHistory.Host")!;
        var register = host.GetMethod("Register")!.CreateDelegate<Action<IStateReaderRegistration>>();
        ReaderRegistrationSink sink = new();
        register(sink);
        register(sink);
        Assert.Equal(4, sink.Bindings.Count);
        Assert.Equal(new[] { 1, 2, 1, 2 }, sink.Bindings.Select(binding => binding.Schema.Version));
        Assert.All(sink.Bindings, binding => Assert.Equal("reader.leaf", binding.Schema.SchemaId));
        Assert.Same(sink.Bindings[0], sink.Bindings[2]);
        Assert.Same(sink.Bindings[1], sink.Bindings[3]);
        Assert.NotSame(sink.Bindings[0], sink.Bindings[1]);
        Assert.Equal(1, sink.Bindings[0].Schema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Int32, Assert.Single(sink.Bindings[0].Schema.BaseSchema!.Fields).TypeTag);
        Assert.Equal(2, sink.Bindings[1].Schema.BaseSchema!.Version);
        Assert.Equal(TypeTag.Byte, Assert.Single(sink.Bindings[1].Schema.BaseSchema!.Fields).TypeTag);
        Assert.Throws<ArgumentNullException>(() => register(null!));
        host.GetMethod("RegisterBase")!.CreateDelegate<Action<IStateReaderRegistration>>()(sink);
        Assert.Equal(new[] { "reader.base", "reader.base" }, sink.Bindings.Skip(4).Select(binding => binding.Schema.SchemaId));
        Assert.Equal(new[] { 1, 2 }, sink.Bindings.Skip(4).Select(binding => binding.Schema.Version));
        Assert.Equal<byte>([0x21, 0x03], host.GetMethod("Historical")!.CreateDelegate<Func<byte[]>>()());

        Type body = assembly.GetType("ReaderHistory.Leaf")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        foreach (int version in new[] { 1, 2 }) {
            FieldInfo field = body.GetField("ReaderV" + version, BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.True(field.IsPrivate && field.IsInitOnly);
            Assert.Equal(body.GetNestedType("V" + version, BindingFlags.NonPublic), Assert.Single(field.FieldType.GenericTypeArguments));
        }
        GeneratorTestRun ordered = RunGenerator(currentSource, files.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(ordered);
        Assert.Equal(GeneratedSource(current, "DurableBinaryBodies.g.cs"), GeneratedSource(ordered, "DurableBinaryBodies.g.cs"));
    }

    [Fact]
    public void GeneratedReaderBindingsKeepMemberOperationsStaticallyBound() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("reader.static", 1)]
            public sealed partial class Model : DurableBase {
                [DurableField(1)] private int _number;
                [DurableField(2)] private string? _name;
            }
            """);
        AssertSchemaOnlyCompiles(run);
        string generated = GeneratedSource(run, "DurableBinaryBodies.g.cs");
        Assert.Contains("StateReaderBinding<V1> ReaderV1 = new(V1.Schema, ReadV1, ApplyDeltaV1, VisitReferences);", generated);
        Assert.Contains("writer.WriteInt32(value.Segment0Field1);", generated);
        Assert.Contains("writer.WriteUInt32(value.Segment0Field2);", generated);
        Assert.Contains("reader.ReadInt32();", generated);
        Assert.Contains("reader.ReadUInt32();", generated);
        Assert.Contains("table.ResolveString(state.Segment0Field2);", generated);
        foreach (string forbidden in new[] { "ValueSlotCodec", "PrimitiveSlotCodecs", "DynamicInvoke", "System.Reflection", "Dictionary<", "StateStore.Storage" }) {
            Assert.DoesNotContain(forbidden, generated);
        }
    }

    [Fact]
    public void GeneratedReaderBindingsAreNotPublishedForReservedHelperCollision() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("reader.invalid", 1)]
            public sealed partial class Invalid : DurableBase { private static class __DurableBinaryBody { } }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
        string generated = BinaryBodyGeneratedText(run);
        Assert.DoesNotContain("RegisterReaders", generated);
        Assert.DoesNotContain("StateReaderBinding", generated);
    }

    private sealed class ReaderRegistrationSink : IStateReaderRegistration {
        internal List<StateReaderBinding> Bindings { get; } = new();
        public void Register(StateReaderBinding binding) => Bindings.Add(binding);
    }
}
