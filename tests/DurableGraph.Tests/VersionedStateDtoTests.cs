using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void StateDtoCaptureOwnsScalarCopyAndGeneratedApiOnlyWritesImmutableDtos() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using System.Buffers;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace BinaryBodies;
            [DurableType("state.copy", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Item : DurableBase {
                [DurableField(8)] private long _wide = -2;
                [DurableField(1)] private int _number = -1;
                [DurableField(3)] private bool _flag = true;
                [Transient] private object _cache = new();
                public void Mutate() { _wide = 900; _number = 100; _flag = false; _cache = new(); }
            }
            public static class Host {
                public static byte[] CaptureThenMutate() {
                    var domain = new Item();
                    var captured = Item.__DurableBinaryBody.Capture(domain);
                    domain.Mutate();
                    var buffer = new ArrayBufferWriter<byte>();
                    var writer = new BinaryPayloadWriter(buffer);
                    Item.__DurableBinaryBody.Write(ref writer, in captured);
                    var current = Item.__DurableBinaryBody.Capture(domain);
                    Item.__DurableBinaryBody.Write(ref writer, in current);
                    return buffer.WrittenSpan.ToArray();
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Assert.Equal<byte>([1, 1, 3, 0xC8, 1, 0, 0x88, 0x0E],
            BinaryBodyDelegate<Func<byte[]>>(assembly, "CaptureThenMutate")());
        Type domain = assembly.GetType("BinaryBodies.Item")!;
        Type body = domain.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Type dto = body.GetNestedType("V1", BindingFlags.NonPublic)!;
        Assert.True(dto.IsValueType);
        Assert.NotNull(dto.GetCustomAttribute<IsReadOnlyAttribute>());
        FieldInfo[] fields = dto.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(new[] { "Segment0Field1", "Segment0Field3", "Segment0Field8" }, fields.Select(field => field.Name).Order().ToArray());
        Assert.All(fields, field => Assert.True(field.IsInitOnly));
        Assert.All(fields, field => Assert.True(field.FieldType.IsValueType));
        Assert.Same(ReadSchemaOnly(domain, 1), dto.GetProperty("Schema", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        MethodInfo write = body.GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!;
        ParameterInfo stateParameter = write.GetParameters()[1];
        Assert.Equal(dto.MakeByRefType(), stateParameter.ParameterType);
        Assert.True(stateParameter.IsIn);
        Assert.Null(body.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic));
        Assert.Equal(dto, body.GetMethod("ReadV1", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(new[] { "AddRoot", "Capture", "ReadV1", "Write" },
            body.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Select(method => method.Name).Order().ToArray());
    }

    [Fact]
    public void StateDtoHistoryKeepsDeletedAndRetypedFieldsWithExactSchemaAndGoldenBytes() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using System.Buffers;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace BinaryBodies;
            [DurableType("state.history", 3, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Item : DurableBase {
                [DurableField(7)] private bool _current = true;
            }
            public static class Host {
                public static byte[] RoundTripVersions() {
                    var reader = new BinaryPayloadReader(new byte[] { 1, 1, 3, 5, 1 });
                    var v1 = Item.__DurableBinaryBody.ReadV1(ref reader);
                    var v2 = Item.__DurableBinaryBody.ReadV2(ref reader);
                    var v3 = Item.__DurableBinaryBody.ReadV3(ref reader);
                    reader.EnsureFullyConsumed();
                    if (v1.Segment0Field1 != -1 || !v1.Segment0Field9 ||
                        v2.Segment0Field1 != -2L || v2.Segment0Field4 != -3 || !v3.Segment0Field7) {
                        throw new Exception("Historical DTO fields do not match their exact layout.");
                    }
                    var buffer = new ArrayBufferWriter<byte>();
                    var writer = new BinaryPayloadWriter(buffer);
                    Item.__DurableBinaryBody.Write(ref writer, in v1);
                    Item.__DurableBinaryBody.Write(ref writer, in v2);
                    Item.__DurableBinaryBody.Write(ref writer, in v3);
                    return buffer.WrittenSpan.ToArray();
                }
            }
            """,
            SnapshotHistory("v1.dgsnapshot", "state.history", 1, (1, 2), (9, 1)),
            SnapshotHistory("v2.dgsnapshot", "state.history", 2, (1, 3), (4, 2)));
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Assert.Equal<byte>([1, 1, 3, 5, 1], BinaryBodyDelegate<Func<byte[]>>(assembly, "RoundTripVersions")());
        Type domain = assembly.GetType("BinaryBodies.Item")!;
        Type body = domain.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        for (int version = 1; version <= 3; version++) {
            Type dto = body.GetNestedType("V" + version, BindingFlags.NonPublic)!;
            Assert.Same(ReadSchemaOnly(domain, version), dto.GetProperty("Schema", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        }

        Type v1 = body.GetNestedType("V1", BindingFlags.NonPublic)!;
        Type v2 = body.GetNestedType("V2", BindingFlags.NonPublic)!;
        Type v3 = body.GetNestedType("V3", BindingFlags.NonPublic)!;
        Assert.Equal(typeof(int), v1.GetField("Segment0Field1", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
        Assert.Equal(typeof(long), v2.GetField("Segment0Field1", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
        Assert.Null(v2.GetField("Segment0Field9", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(v3.GetField("Segment0Field1", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(v3, body.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceCaptureStateDtoSupportsHistoricalStringIncludingRemovedAncestor(bool inAncestor) {
        AdditionalText historical = SnapshotHistory("old.dgsnapshot", inAncestor ? "state.old-base" : "state.item", 1, (1, 4));
        AdditionalText[] history = inAncestor ? [
            historical,
            StateDtoHistory("state.item", 1, "state.old-base", 1),
        ] : [historical];
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("state.item", 2, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Item : DurableBase { [DurableField(1)] private int _current; }
            """, history);
        AssertSchemaOnlyCompiles(run);
        Type body = EmitAndLoad(run.OutputCompilation).GetType("Item")!
            .GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Assert.Equal(typeof(uint), body.GetNestedType("V1", BindingFlags.NonPublic)!
            .GetField("Segment0Field1", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
        Assert.Single(body.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.GetParameters());
    }

    [Fact]
    public void ReferenceCaptureStateDtoScalarLeafKeepsContextFreeCaptureWithHistoricalBaseString() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("state.base", 2, SchemaOnly = true, GenerateBinaryBody = true)]
            public abstract partial class Base : DurableBase { [DurableField(1)] private int _current; }
            [DurableType("state.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : Base { [DurableField(2)] private bool _flag; }
            """, SnapshotHistory("base-v1.dgsnapshot", "state.base", 1, (1, 4)));
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type baseBody = assembly.GetType("Base")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Assert.Equal(typeof(uint), baseBody.GetNestedType("V1", BindingFlags.NonPublic)!
            .GetField("Segment0Field1", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
        Type leafBody = assembly.GetType("Leaf")!.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Assert.Single(leafBody.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.GetParameters());
    }

    [Fact]
    public void StateDtoEmptyLayoutReturnsCompletedValueWithoutConsumingBytes() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using System.Buffers;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace BinaryBodies;
            [DurableType("state.empty", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Item : DurableBase { }
            public static class Host {
                public static int[] RoundTrip() {
                    var reader = new BinaryPayloadReader(new byte[] { 99 });
                    var state = Item.__DurableBinaryBody.ReadV1(ref reader);
                    var buffer = new ArrayBufferWriter<byte>();
                    var writer = new BinaryPayloadWriter(buffer);
                    Item.__DurableBinaryBody.Write(ref writer, in state);
                    var captured = Item.__DurableBinaryBody.Capture(new Item());
                    Item.__DurableBinaryBody.Write(ref writer, in captured);
                    return [reader.ConsumedCount, reader.RemainingCount, buffer.WrittenCount];
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Assert.Equal<int>([0, 1, 0], BinaryBodyDelegate<Func<int[]>>(EmitAndLoad(run.OutputCompilation), "RoundTrip")());
    }

    private static AdditionalText StateDtoHistory(string schemaId, int version, string baseId, int baseVersion) {
        string path = $"{schemaId}-{version}.dgsnapshot";
        string content = SnapshotHistory(path, schemaId, version, (1, 2)).GetText()!.ToString();
        string versionLine = $"// version:{version}\n";
        content = content.Replace(versionLine, versionLine +
            $"// base:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(baseId))}|{baseVersion}\n");
        return new InMemoryAdditionalText(path, content);
    }
}
