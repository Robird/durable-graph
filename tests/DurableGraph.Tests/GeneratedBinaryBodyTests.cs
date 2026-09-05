using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    private const string BinaryBodyChain = """
        using System;
        using System.Buffers;
        using System.IO;
        using Atelia.DurableGraph;
        using Atelia.DurableGraph.StateStore.Serialization;
        namespace BinaryBodies;

        [DurableType("body.base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public abstract partial class Base : DurableBase {
            [DurableField(9)] private int _number;
            [Transient] private int _cache = 73;
            [DurableField(2)] private bool _flag;
            protected Base(bool flag, int number) { _flag = flag; _number = number; }
            public bool Flag => _flag;
            public int Number => _number;
            public int Cache => _cache;
        }
        [DurableType("body.middle", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public partial class Middle : Base {
            [DurableField(2)] private long _wide;
            protected Middle(bool flag, int number, long wide) : base(flag, number) { _wide = wide; }
            public long Wide => _wide;
        }
        [DurableType("body.empty", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public partial class Empty : Middle {
            protected Empty(bool flag, int number, long wide) : base(flag, number, wide) { }
        }
        [DurableType("body.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public sealed partial class Leaf : Empty {
            [DurableField(2)] private int _last;
            public Leaf(bool flag, int number, long wide, int last) : base(flag, number, wide) { _last = last; }
            public int Last => _last;
        }
        public static class Host {
            public static byte[] Write(bool flag, int number, long wide, int last) {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new BinaryPayloadWriter(buffer);
                Leaf.__DurableBinaryBody.Write(ref writer, new Leaf(flag, number, wide, last));
                return buffer.WrittenSpan.ToArray();
            }
            public static long[] Read(byte[] bytes, bool requireEnd) {
                var value = new Leaf(false, 101, 102, 103);
                var reader = new BinaryPayloadReader(bytes);
                int error = 0;
                try {
                    Leaf.__DurableBinaryBody.Read(ref reader, value);
                    if (requireEnd) { reader.EnsureFullyConsumed(); }
                }
                catch (EndOfStreamException) { error = 1; }
                catch (InvalidDataException) { error = 2; }
                return [error, reader.ConsumedCount, reader.RemainingCount,
                    value.Flag ? 1 : 0, value.Number, value.Wide, value.Last, value.Cache];
            }
            public static int[] NullBeforeIO() {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new BinaryPayloadWriter(buffer);
                var reader = new BinaryPayloadReader(new byte[] { 1, 2 });
                int writeError = 0;
                int readError = 0;
                try { Leaf.__DurableBinaryBody.Write(ref writer, null!); }
                catch (ArgumentNullException) { writeError = 1; }
                try { Leaf.__DurableBinaryBody.Read(ref reader, null!); }
                catch (ArgumentNullException) { readError = 1; }
                return [writeError, readError, buffer.WrittenCount, reader.ConsumedCount];
            }
            public static byte[] WriteBaseOfDerived() {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new BinaryPayloadWriter(buffer);
                Base.__DurableBinaryBody.Write(ref writer, new Leaf(true, -1, 999, 888));
                return buffer.WrittenSpan.ToArray();
            }
            public static byte[] FailedWrite() {
                var buffer = new FailAfterFirstAdvance();
                var writer = new BinaryPayloadWriter(buffer);
                try { Leaf.__DurableBinaryBody.Write(ref writer, new Leaf(true, int.MinValue, 999, 888)); }
                catch (IOException) { return buffer.Buffer.WrittenSpan.ToArray(); }
                throw new Exception("Expected downstream failure.");
            }
            private sealed class FailAfterFirstAdvance : IBufferWriter<byte> {
                internal readonly ArrayBufferWriter<byte> Buffer = new();
                private int _advances;
                public void Advance(int count) {
                    if (_advances++ == 1) { throw new IOException("Injected write failure."); }
                    Buffer.Advance(count);
                }
                public Memory<byte> GetMemory(int sizeHint = 0) => Buffer.GetMemory(sizeHint);
                public Span<byte> GetSpan(int sizeHint = 0) => Buffer.GetSpan(sizeHint);
            }
        }
        """;

    [Fact]
    public void BinaryBodyExecutesPrivateBaseFirstFieldsInLocalIdOrderWithGoldenExtremes() {
        GeneratorTestRun run = RunGenerator(BinaryBodyChain);
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        var write = BinaryBodyDelegate<Func<bool, int, long, int, byte[]>>(assembly, "Write");
        var read = BinaryBodyDelegate<Func<byte[], bool, long[]>>(assembly, "Read");
        byte[] minimum = [1, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1,
            0xFE, 0xFF, 0xFF, 0xFF, 0x0F];
        byte[] maximum = [0, 0xFE, 0xFF, 0xFF, 0xFF, 0x0F,
            0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1,
            0xFF, 0xFF, 0xFF, 0xFF, 0x0F];
        Assert.Equal(minimum, write(true, int.MinValue, long.MinValue, int.MaxValue));
        Assert.Equal(maximum, write(false, int.MaxValue, long.MaxValue, int.MinValue));
        Assert.Equal<long>([0, 21, 0, 1, int.MinValue, long.MinValue, int.MaxValue, 73], read(minimum, true));
        Assert.Equal<long>([0, 21, 0, 0, int.MaxValue, long.MaxValue, int.MinValue, 73], read(maximum, true));
        Assert.Equal<byte>([1, 1], BinaryBodyDelegate<Func<byte[]>>(assembly, "WriteBaseOfDerived")());

        string generated = BinaryBodyGeneratedText(run);
        foreach (string forbidden in new[] { "ValueSlotCodec", "PrimitiveSlotCodecs", "typeof(", "DynamicInvoke", "delegate", "(object)", "System.Reflection" }) {
            Assert.DoesNotContain(forbidden, generated);
        }
        Assert.Contains("global::BinaryBodies.Base.__DurableBinaryBody.Write(ref writer, value)", generated);
        Assert.Contains("global::BinaryBodies.Empty.__DurableBinaryBody.Read(ref reader, value)", generated);
        Assert.Contains("writer.WriteBoolean(value._flag)", generated);
        Assert.Contains("writer.WriteInt32(value._number)", generated);
        Assert.Contains("writer.WriteInt64(value._wide)", generated);
        Assert.DoesNotContain("value._cache", generated);
        Assert.DoesNotContain("Serializer", generated);
    }

    [Fact]
    public void BinaryBodyNullAndReadFailuresPreserveDocumentedPartialStateAndOuterBoundary() {
        GeneratorTestRun run = RunGenerator(BinaryBodyChain);
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Assert.Equal<int>([1, 1, 0, 0], BinaryBodyDelegate<Func<int[]>>(assembly, "NullBeforeIO")());
        Assert.Equal<byte>([1], BinaryBodyDelegate<Func<byte[]>>(assembly, "FailedWrite")());
        var read = BinaryBodyDelegate<Func<byte[], bool, long[]>>(assembly, "Read");
        Assert.Equal<long>([2, 0, 1, 0, 101, 102, 103, 73], read([2], true));
        // The Boolean succeeded; a truncated Int32 does not consume its first byte or assign its field.
        Assert.Equal<long>([1, 1, 1, 1, 101, 102, 103, 73], read([1, 0x80], true));
        // Base and middle succeeded; leaf failure preserves both earlier declaration layers.
        Assert.Equal<long>([1, 3, 1, 1, -1, -2, 103, 73], read([1, 1, 3, 0x80], true));
        Assert.Equal<long>([0, 4, 1, 1, -1, -2, -3, 73], read([1, 1, 3, 5, 99], false));
        Assert.Equal<long>([2, 4, 1, 1, -1, -2, -3, 73], read([1, 1, 3, 5, 99], true));
    }

    [Fact]
    public void BinaryBodyInvalidBooleanDoesNotUndoAnEarlierField() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using System.IO;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace BinaryBodies;
            [DurableType("body.boolean", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Item : DurableBase {
                [DurableField(2)] private bool _flag = true;
                [DurableField(1)] private int _number = 73;
                public bool Flag => _flag;
                public int Number => _number;
            }
            public static class Host {
                public static int[] Read() {
                    var reader = new BinaryPayloadReader(new byte[] { 1, 2 });
                    var value = new Item();
                    try { Item.__DurableBinaryBody.Read(ref reader, value); }
                    catch (InvalidDataException) {
                        return [value.Number, value.Flag ? 1 : 0, reader.ConsumedCount, reader.RemainingCount];
                    }
                    throw new Exception("Expected invalid Boolean failure.");
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Assert.Equal<int>([-1, 1, 1, 1], BinaryBodyDelegate<Func<int[]>>(EmitAndLoad(run.OutputCompilation), "Read")());
    }

    [Theory]
    [InlineData("SchemaOnly = true", "[DurableField(1)] private string _text = string.Empty;")]
    [InlineData("", "[DurableField(1)] private int _number;")]
    [InlineData("SchemaOnly = true", "private static class __DurableBinaryBody { }")]
    public void BinaryBodyRejectsUnsupportedOptInWithoutPublishingBody(string flags, string members) {
        string options = string.IsNullOrEmpty(flags) ? "" : flags + ", ";
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            [DurableType("body.invalid", 1, {{options}}GenerateBinaryBody = true)]
            public sealed partial class Invalid : DurableBase { {{members}} }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
        Assert.DoesNotContain("static void Write(", BinaryBodyGeneratedText(run));
    }

    [Fact]
    public void BinaryBodyRejectsMissingAncestorOptIn() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("body.base", 1, SchemaOnly = true)]
            public abstract partial class Base : DurableBase { [DurableField(1)] private int _base; }
            [DurableType("body.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : Base { [DurableField(1)] private int _leaf; }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
        Assert.DoesNotContain("static void Write(", BinaryBodyGeneratedText(run));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BinaryBodyDoesNotChangeNonOptInSchemaOnlyOrLegacyBehavior(bool schemaOnly) {
        GeneratorTestRun run = RunGenerator($$"""
            using Atelia.DurableGraph;
            [DurableType("body.unchanged", 1, SchemaOnly = {{schemaOnly.ToString().ToLowerInvariant()}})]
            public sealed partial class Unchanged : DurableBase { [DurableField(1)] private string _text = "hello"; }
            """);
        AssertSchemaOnlyCompiles(run);
        string generated = BinaryBodyGeneratedText(run);
        Assert.DoesNotContain("__DurableBinaryBody", generated);
        Type type = EmitAndLoad(run.OutputCompilation).GetType("Unchanged")!;
        Assert.Equal(!schemaOnly, type.GetProperty("Serializer", BindingFlags.Public | BindingFlags.Static) is not null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BinaryBodyRequiresSuccessfulOwnAndAncestorHistory(bool ancestor, bool missingHistory) {
        string source = ancestor ? $$"""
            using Atelia.DurableGraph;
            [DurableType("body.base", {{(missingHistory ? 2 : 1)}}, SchemaOnly = true, GenerateBinaryBody = true)]
            public abstract partial class Base : DurableBase { [DurableField(1)] private long _base; }
            [DurableType("body.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : Base { [DurableField(1)] private int _leaf; }
            """ : $$"""
            using Atelia.DurableGraph;
            [DurableType("body.base", {{(missingHistory ? 2 : 1)}}, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Base : DurableBase { [DurableField(1)] private long _base; }
            """;
        AdditionalText[] history = missingHistory ? [] : [SnapshotHistory("old.dgsnapshot", "body.base", 1, (1, 2))];
        GeneratorTestRun run = RunGenerator(source, history);
        Assert.Contains(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain("static void Write(", BinaryBodyGeneratedText(run));
    }

    [Fact]
    public void BinaryBodyRejectsPrimitiveLeafWhenOptedInAncestorContainsString() {
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("body.base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public abstract partial class Base : DurableBase { [DurableField(1)] private string _text = "base"; }
            [DurableType("body.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : Base { [DurableField(1)] private int _leaf; }
            """);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DG0020");
        Assert.DoesNotContain(run.GeneratedSources, source => source.HintName == "DurableBinaryBodies.g.cs");
        Assert.DoesNotContain("static void Write(", BinaryBodyGeneratedText(run));
    }

    [Theory]
    [InlineData(false, "DG0012")]
    [InlineData(true, "DG0013")]
    public void BinaryBodyRejectsUnrelatedHistoryParseOrDuplicateConflict(bool duplicateConflict, string diagnosticId) {
        AdditionalText[] history = duplicateConflict ? [
            SnapshotHistory("first.dgsnapshot", "unrelated", 1, (1, 2)),
            SnapshotHistory("second.dgsnapshot", "unrelated", 1, (1, 3)),
        ] : [
            new InMemoryAdditionalText("malformed.dgsnapshot", "// durable-graph-snapshot:1\n// not-a-snapshot\n"),
        ];
        GeneratorTestRun run = RunGenerator("""
            using Atelia.DurableGraph;
            [DurableType("body.valid", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Valid : DurableBase { [DurableField(1)] private int _value; }
            """, history);
        Assert.Contains(run.GeneratorDiagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.DoesNotContain(run.GeneratedSources, source => source.HintName == "DurableBinaryBodies.g.cs");
        Assert.DoesNotContain("static void Write(", BinaryBodyGeneratedText(run));
    }

    [Fact]
    public void BinaryBodyWritesCurrentVersionWhileHistoricalSchemaRemainsMetadataOnly() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using System.Buffers;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.StateStore.Serialization;
            namespace BinaryBodies;
            [DurableType("body.versioned", 2, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Versioned : DurableBase {
                [DurableField(1)] private long _value = long.MinValue;
            }
            public static class Host {
                public static byte[] Write() {
                    var buffer = new ArrayBufferWriter<byte>();
                    var writer = new BinaryPayloadWriter(buffer);
                    Versioned.__DurableBinaryBody.Write(ref writer, new Versioned());
                    return buffer.WrittenSpan.ToArray();
                }
            }
            """, SnapshotHistory("v1.dgsnapshot", "body.versioned", 1, (1, 2)));
        AssertSchemaOnlyCompiles(run);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type type = assembly.GetType("BinaryBodies.Versioned")!;
        Assert.Equal(TypeTag.Int32, Assert.Single(ReadSchemaOnly(type, 1).Fields).TypeTag);
        Assert.Equal(TypeTag.Int64, Assert.Single(ReadSchemaOnly(type, 2).Fields).TypeTag);
        Assert.Equal<byte>([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1],
            BinaryBodyDelegate<Func<byte[]>>(assembly, "Write")());
        Type body = type.GetNestedType("__DurableBinaryBody", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { "Read", "Write" }, body.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain("Upgrade", BinaryBodyGeneratedText(run));
        Assert.DoesNotContain("__DurableSnapshot", BinaryBodyGeneratedText(run));
    }

    private static T BinaryBodyDelegate<T>(Assembly assembly, string method) where T : Delegate =>
        assembly.GetType("BinaryBodies.Host")!.GetMethod(method)!.CreateDelegate<T>();

    private static string BinaryBodyGeneratedText(GeneratorTestRun run) =>
        string.Join("\n", run.GeneratedSources.Select(source => source.SourceText.ToString()));
}
