using System.Buffers;
using System.Reflection;
using Atelia.DurableGraph.Build;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void PersistedDeltaChainColdReopenReconstructsFrozenCaptureAndValidatesTargetReferences() {
        GeneratorTestRun run = RunGenerator(PersistedDeltaCaptureSource);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("PersistedCapture.Host")!;
        var capture = host.GetMethod("Capture")!.CreateDelegate<PersistedCapture>();
        var decode = host.GetMethod("Decode")!.CreateDelegate<PersistedDecode>();
        var input = capture(); // All domain instances, candidate DTOs and CaptureSession stay inside this call.
        DurableSchema schema = input.Schema;
        uint ownerId = input.Owner;
        uint nameId = input.Name;
        uint aliasId = input.Alias;
        uint emptyId = input.Empty;
        byte[][] expected = [ExpectedCapturedBody(nameId, nameId, emptyId, 1),
            ExpectedCapturedBody(nameId, aliasId, emptyId, 2), ExpectedCapturedBody(nameId, aliasId, emptyId, 3)];
        Assert.Equal(expected[0], input.Base);
        Assert.True(input.First.HasChanges);
        Assert.True(input.Second.HasChanges);
        Assert.Equal<byte>([0x0A, checked((byte)aliasId), 4], input.First.Payload.ToArray());
        Assert.Equal<byte>([0x08, 6], input.Second.Payload.ToArray());

        // This is explicitly a fixture directory, never a persistent SchemaStore/type manifest.
        Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor> metadata = [];
        using RawBaseDirectory directory = new();
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat, SegmentSizeThresholdBytes = 8 };
        FrameAddress first, second, third, repeated, missingString, wrongKind, malformed;
        using (SegmentStore segments = SegmentStore.CreateNew(directory.Path, options)) {
            StateRevisionStore store = new(segments);
            first = store.Append(StateRevision.CreateBase(null,
                input.Strings.Where(item => item.Id != aliasId).Select(item => ObjectVersionRecord.CreateBase(item.Id, item.Body))
                    .Append(ObjectVersionRecord.CreateBase(ownerId, input.Base)), []));
            foreach (var item in input.Strings.Where(item => item.Id != aliasId)) metadata.Add((first, item.Id), new(true, null, "string-v1"));
            metadata.Add((first, ownerId), new(false, schema, "generated-v1"));
            second = store.Append(StateRevision.CreateDelta(first,
                [ObjectVersionRecord.CreateDelta(ownerId, first, input.First.Payload),
                    ObjectVersionRecord.CreateBase(aliasId, input.Strings.Single(item => item.Id == aliasId).Body)], []));
            metadata.Add((second, aliasId), new(true, null, "string-v1"));
            third = store.Append(StateRevision.CreateDelta(second,
                [ObjectVersionRecord.CreateDelta(ownerId, second, input.Second.Payload)], []));
            metadata.Add((second, ownerId), new(false, schema, "generated-v1"));
            metadata.Add((third, ownerId), new(false, schema, "generated-v1"));

            // Reuse the same prepared bytes on another branch; no serialization is repeated.
            repeated = store.Append(StateRevision.CreateDelta(first,
                [ObjectVersionRecord.CreateDelta(ownerId, first, input.First.Payload),
                    ObjectVersionRecord.CreateBase(aliasId, input.Strings.Single(item => item.Id == aliasId).Body)], []));
            metadata.Add((repeated, ownerId), new(false, schema, "generated-v1"));
            metadata.Add((repeated, aliasId), new(true, null, "string-v1"));
            Assert.Equal(input.First.Payload.ToArray(), store.ReadObjectVersionChain(repeated, ownerId).Records[^1].Record.Body.ToArray());
            missingString = store.Append(StateRevision.CreateDelta(third, [], [nameId]));
            wrongKind = store.Append(StateRevision.CreateDelta(third,
                [ObjectVersionRecord.CreateBase(nameId, expected[2])], []));
            metadata.Add((wrongKind, nameId), new(false, schema, "generated-v1"));
            malformed = store.Append(StateRevision.CreateDelta(third,
                [ObjectVersionRecord.CreateDelta(ownerId, third, new byte[] { 8, 8, 0 })], []));
            metadata.Add((malformed, ownerId), new(false, schema, "generated-v1"));
        }
        Assert.NotEqual(first.FileNumber, second.FileNumber);
        Assert.NotEqual(second.FileNumber, third.FileNumber);
        Array.Clear(input.Base);
        foreach (var item in input.Strings) Array.Clear(item.Body);
        input = default; // Drop prepared bodies as well; only addresses, metadata and independent expectations survive.

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(directory.Path, options);
        StateRevisionStore cold = new(reopened);
        byte[] Load(FrameAddress revision, Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor> directoryMetadata) {
            ObjectVersionChain chain = cold.ReadObjectVersionChain(revision, ownerId);
            PreflightTypedChain(chain, directoryMetadata, schema, "generated-v1");
            Dictionary<uint, byte[]> strings = [];
            foreach ((uint id, FrameAddress address) in cold.ReadLiveObjectHeads(revision)) {
                if (!directoryMetadata.TryGetValue((address, id), out var descriptor))
                    throw new InvalidDataException("Missing exact fixture descriptor.");
                if (descriptor.IsString) {
                    if (descriptor.Schema is not null || descriptor.Codec != "string-v1")
                        throw new InvalidDataException("Wrong string fixture contract.");
                    strings.Add(id, cold.ReadObjectBase(revision, id));
                }
            }
            return decode(chain.Records.Select(entry => entry.Record.Body.ToArray()).ToArray(), strings);
        }
        Assert.Equal(expected[0], Load(first, metadata));
        Assert.Equal(expected[1], Load(second, metadata));
        Assert.Equal(expected[2], Load(third, metadata));
        Assert.Equal(expected[1], Load(repeated, metadata));
        ObjectVersionChain oldest = cold.ReadObjectVersionChain(first, ownerId);
        ObjectVersionChain latest = cold.ReadObjectVersionChain(third, ownerId);
        Assert.Single(oldest.Records);
        Assert.Equal(new[] { first, second, third }, latest.Records.Select(entry => entry.Address));
        Assert.Equal(third, latest.HeadAddress);
        Assert.Equal(ownerId, latest.ObjectId);
        Assert.Equal(latest.Records.Sum(entry => (long)entry.PayloadBytes), latest.ReconstructionBytes);
        Assert.True(latest.ReconstructionBytes > oldest.ReconstructionBytes);

        // Every descriptor is preflighted before even Base decoding starts. Poison the last one
        // and observe zero decoder calls, rather than relying on an Apply type/value accident.
        foreach (var badDescriptor in new DeltaFixtureDescriptor?[] {
            null, new(true, null, "string-v1"), new(false, schema, "other-codec"),
            new(false, new DurableSchema(schema.SchemaId, 2, schema.Fields.ToArray(), schema.BaseSchema), "generated-v1"),
        }) {
            var wrong = new Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor>(metadata);
            if (badDescriptor is null) wrong.Remove((third, ownerId));
            else wrong[(third, ownerId)] = badDescriptor;
            int calls = (int)host.GetField("DecodeCalls")!.GetValue(null)!;
            Assert.Throws<InvalidDataException>(() => Load(third, wrong));
            Assert.Equal(calls, (int)host.GetField("DecodeCalls")!.GetValue(null)!);
        }
        foreach (FrameAddress bad in new[] { missingString, wrongKind, malformed }) {
            byte[]? delivered = null;
            Assert.Throws<InvalidDataException>(() => delivered = Load(bad, metadata));
            Assert.Null(delivered); // A late Apply/full-consumption/reference failure returns no partial result.
        }
        Assert.Equal(expected[0], Load(first, metadata));
        Assert.Equal(expected[2], Load(third, metadata));
    }

    [Fact]
    public void PersistedHistoricalDeltaChainUsesRemovedAncestorLayoutAndExactVersionDescriptors() {
        using AncestryHistoryDirectory files = new();
        SnapshotHistoryTool publisher = new();
        GeneratorTestRun initial = RunGenerator(FusedDeltaPreamble + """
            [DurableType("persisted.base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public abstract partial class OldBase : DurableBase { [DurableField(99)] private int _number; }
            [DurableType("persisted.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : OldBase { [DurableField(99)] private bool _flag; }
            """);
        AssertSchemaOnlyCompiles(initial);
        publisher.Publish(files.WriteManifest(initial), files.History);
        string source = FusedDeltaPreamble + """
            [DurableType("persisted.base", 2, SchemaOnly = true, GenerateBinaryBody = true)]
            public abstract partial class NewBase : DurableBase { [DurableField(2)] private byte _small; }
            [DurableType("persisted.leaf", 2, SchemaOnly = true, GenerateBinaryBody = true)]
            public sealed partial class Leaf : NewBase { [DurableField(1)] private uint _number; }
            public static class Host {
            """ + FusedDeltaHostMethods("Leaf", 1) + FusedDeltaHostMethods("Leaf", 2) + "\n}";
        GeneratorTestRun current = RunGenerator(source, files.ReadAdditionalTexts());
        AssertSchemaOnlyCompiles(current);
        Assembly assembly = EmitAndLoad(current.OutputCompilation);
        Assert.Null(assembly.GetType("FusedDelta.OldBase"));
        Type leaf = assembly.GetType("FusedDelta.Leaf")!;
        DurableSchema oldSchema = ReadSchemaOnly(leaf, 1);
        DurableSchema newSchema = ReadSchemaOnly(leaf, 2);
        Assert.Equal(TypeTag.Int32, Assert.Single(oldSchema.BaseSchema!.Fields).TypeTag);
        Assert.Equal(TypeTag.Byte, Assert.Single(newSchema.BaseSchema!.Fields).TypeTag);
        Type host = assembly.GetType("FusedDelta.Host")!;
        var prepare = host.GetMethod("Prepare1")!.CreateDelegate<Func<byte[], byte[], PreparedDelta>>();
        var apply = host.GetMethod("Apply1")!.CreateDelegate<Func<byte[], byte[], byte[]>>();
        byte[] baseBytes = [2, 0]; // Old ancestor int=1, leaf bool=false.
        PreparedDelta delta1 = prepare(baseBytes, [2, 1]);
        PreparedDelta delta2 = prepare([2, 1], [4, 1]);
        Assert.Equal<byte>([2, 1], delta1.Payload.ToArray());
        Assert.Equal<byte>([1, 4], delta2.Payload.ToArray());
        Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor> metadata = [];
        using RawBaseDirectory directory = new();
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat, SegmentSizeThresholdBytes = 8 };
        FrameAddress first, second, third;
        using (SegmentStore segments = SegmentStore.CreateNew(directory.Path, options)) {
            StateRevisionStore store = new(segments);
            first = store.Append(StateRevision.CreateBase(null, [ObjectVersionRecord.CreateBase(1, baseBytes)], []));
            second = store.Append(StateRevision.CreateDelta(first, [ObjectVersionRecord.CreateDelta(1, first, delta1.Payload)], []));
            third = store.Append(StateRevision.CreateDelta(second, [ObjectVersionRecord.CreateDelta(1, second, delta2.Payload)], []));
            foreach (FrameAddress address in new[] { first, second, third }) metadata.Add((address, 1), new(false, oldSchema, "generated-v1"));
        }
        Array.Clear(baseBytes);
        delta1 = null!;
        delta2 = null!;
        Assert.NotEqual(first.FileNumber, third.FileNumber);
        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(directory.Path, options);
        StateRevisionStore cold = new(reopened);
        byte[] Load(FrameAddress revision) {
            ObjectVersionChain chain = cold.ReadObjectVersionChain(revision, 1);
            PreflightTypedChain(chain, metadata, oldSchema, "generated-v1");
            byte[] state = chain.Records[0].Record.Body.ToArray();
            foreach (var entry in chain.Records.Skip(1)) state = apply(state, entry.Record.Body.ToArray());
            return state;
        }
        Assert.Equal<byte>([2, 1], Load(second));
        Assert.Equal<byte>([4, 1], Load(third));
        metadata[(second, 1)] = new(false, newSchema, "generated-v2");
        Assert.Throws<InvalidDataException>(() => Load(third));
        metadata[(second, 1)] = new(false, oldSchema, "generated-v1");
        Assert.Equal<byte>([4, 1], Load(third));
    }

    private sealed record DeltaFixtureDescriptor(bool IsString, DurableSchema? Schema, string Codec);

    private static void PreflightTypedChain(ObjectVersionChain chain,
        Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor> metadata, DurableSchema schema, string codec) {
        foreach (var entry in chain.Records) {
            if (!metadata.TryGetValue((entry.Address, chain.ObjectId), out var descriptor) || descriptor.IsString ||
                !schema.Equals(descriptor.Schema) || descriptor.Codec != codec)
                throw new InvalidDataException("Every exact locator must have the expected kind, exact Schema and codec before decoding.");
        }
    }

    private static byte[] ExpectedCapturedBody(uint name, uint alias, uint empty, int score) {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BinaryPayloadWriter(buffer);
        writer.WriteUInt32(name);
        writer.WriteUInt32(alias);
        writer.WriteUInt32(empty);
        writer.WriteInt32(score);
        return buffer.WrittenSpan.ToArray();
    }

    private delegate (uint Owner, uint Name, uint Alias, uint Empty, DurableSchema Schema,
        (uint Id, byte[] Body)[] Strings, byte[] Base, PreparedDelta First, PreparedDelta Second) PersistedCapture();
    private delegate byte[] PersistedDecode(byte[][] chain, Dictionary<uint, byte[]> strings);

    private const string PersistedDeltaCaptureSource = """
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using Atelia.DurableGraph;
        using Atelia.DurableGraph.StateStore.Serialization;
        namespace PersistedCapture;
        [DurableType("persisted.capture.base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public abstract partial class Base : DurableBase {
            [DurableField(1)] private string _name;
            protected Base(string name) { _name = name; }
            public void Rename(string name) { _name = name; }
        }
        [DurableType("persisted.capture.leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public sealed partial class Leaf : Base {
            [DurableField(1)] private string _alias;
            [DurableField(9)] private string _empty = string.Empty;
            [DurableField(20)] private int _score = 1;
            public Leaf(string shared) : base(shared) { _alias = shared; }
            public void Change(string alias, int score) { _alias = alias; _score = score; }
        }
        public static class Host {
            public static int DecodeCalls;
            private static byte[] Write(in Leaf.__DurableBinaryBody.V1 state) {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new BinaryPayloadWriter(buffer);
                Leaf.__DurableBinaryBody.Write(ref writer, in state);
                return buffer.WrittenSpan.ToArray();
            }
            public static (uint Owner, uint Name, uint Alias, uint Empty, DurableSchema Schema,
                (uint Id, byte[] Body)[] Strings, byte[] Base, PreparedDelta First, PreparedDelta Second) Capture() {
                string shared = new string(new[] { 'x' });
                string equal = new string(new[] { 'x' });
                if (ReferenceEquals(shared, equal)) throw new Exception("Distinct fixture strings.");
                var owner = new Leaf(shared);
                var session = new CaptureSession();
                CapturedGraph Capture() {
                    var context = session.BeginCapture();
                    Leaf.__DurableBinaryBody.AddRoot(context, owner);
                    return context.Seal();
                }
                var first = Capture();
                owner.Rename("mutation after Seal"); owner.Change("mutation", 90);
                var prior = first.Objects.Single(item => item.Kind == CapturedObjectKind.Durable).GetState<Leaf.__DurableBinaryBody.V1>();
                session.Accept(first); // Fixture's in-memory baseline only; not a durable publication.
                owner.Rename(shared); owner.Change(equal, 2);
                var second = Capture();
                owner.Rename("mutation after Seal"); owner.Change("mutation", 91);
                var middle = second.Objects.Single(item => item.Kind == CapturedObjectKind.Durable).GetState<Leaf.__DurableBinaryBody.V1>();
                var delta1 = Leaf.__DurableBinaryBody.PrepareDelta(in prior, in middle);
                session.Accept(second);
                owner.Rename(shared); owner.Change(equal, 3);
                var third = Capture();
                owner.Rename("mutation after Seal"); owner.Change("mutation", 92);
                var current = third.Objects.Single(item => item.Kind == CapturedObjectKind.Durable).GetState<Leaf.__DurableBinaryBody.V1>();
                var delta2 = Leaf.__DurableBinaryBody.PrepareDelta(in middle, in current);
                var strings = third.Objects.Where(item => item.Kind == CapturedObjectKind.String).Select(item => {
                    var buffer = new ArrayBufferWriter<byte>();
                    var writer = new BinaryPayloadWriter(buffer);
                    writer.WriteString(item.StringContent);
                    return (item.Id, buffer.WrittenSpan.ToArray());
                }).ToArray();
                session.Discard(third);
                return (first.RootIds[0], prior.Segment0Field1, middle.Segment1Field1, prior.Segment1Field9,
                    Leaf.__DurableBinaryBody.V1.Schema, strings, Write(in prior), delta1, delta2);
            }
            public static byte[] Decode(byte[][] chain, Dictionary<uint, byte[]> stringBodies) {
                DecodeCalls++;
                var reader = new BinaryPayloadReader(chain[0]);
                var state = Leaf.__DurableBinaryBody.ReadV1(ref reader);
                reader.EnsureFullyConsumed();
                for (int i = 1; i < chain.Length; i++) {
                    var deltaReader = new BinaryPayloadReader(chain[i]);
                    state = Leaf.__DurableBinaryBody.ApplyDeltaV1(ref deltaReader, in state);
                    deltaReader.EnsureFullyConsumed();
                }
                var strings = StringReadTable.Decode(stringBodies.Select(item => (item.Key, (ReadOnlyMemory<byte>)item.Value)));
                Leaf.__DurableBinaryBody.ValidateStringReferences(in state, strings);
                string name = strings.ResolveString(state.Segment0Field1)!;
                string alias = strings.ResolveString(state.Segment1Field1)!;
                if (name != "x" || alias != "x" || ReferenceEquals(name, alias) != (state.Segment1Field20 == 1) ||
                    !ReferenceEquals(strings.ResolveString(state.Segment1Field9), string.Empty))
                    throw new InvalidDataException("Restored identity or frozen values.");
                return Write(in state); // No result escapes before every body and reference check succeeds.
            }
        }
        """;
}
