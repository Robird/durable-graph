using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void PreparedRevisionPlannerPersistsFrozenGeneratedBodiesAndRebasesUnchangedObjects() {
        GeneratorTestRun run = RunGenerator(PreparedRevisionSource);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("PreparedRevisionWitness.Host")!;
        var capture = host.GetMethod("Capture")!.CreateDelegate<PreparedRevisionCapture>();
        var decode = host.GetMethod("Decode")!.CreateDelegate<PreparedRevisionDecode>();
        var session = (CaptureSession)host.GetField("Session")!.GetValue(null)!;

        // Exact types and roots belong to this fixture, not to the opaque Storage wire format.
        Dictionary<(FrameAddress Address, uint Id), DeltaFixtureDescriptor> metadata = [];
        FrameAddress[] revisions = new FrameAddress[5];
        byte[][] expected = new byte[5][];
        uint[][] roots = new uint[5][];
        DurableSchema? schema = null;
        uint ownerId = 0, originalStringId = 0, equalStringId = 0, emptyId = 0;
        long initialH = 0, accumulatedH = 0;
        using RawBaseDirectory directory = new();
        RbfSegmentStoreOptions options = new() { NewStoreLayout = RbfSegmentStoreLayout.Flat, SegmentSizeThresholdBytes = 8 };
        using (SegmentStore segments = SegmentStore.CreateNew(directory.Path, options)) {
            StateRevisionStore store = new(segments);
            for (int stage = 0; stage < revisions.Length; stage++) {
                CapturedGraph? accepted = session.Current;
                var input = capture(stage);
                Assert.Same(accepted, session.Current);
                Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
                roots[stage] = input.Graph.RootIds.ToArray();
                Assert.Equal(new[] { input.Graph.RootIds[0], input.Graph.RootIds[0], 0u }, roots[stage]);
                var owner = Assert.Single(input.Rows, row => !row.IsString);
                if (stage == 0) {
                    ownerId = owner.Id;
                    schema = owner.Schema!;
                    originalStringId = Assert.Single(input.Graph.Objects,
                        item => item.Kind == CapturedObjectKind.String && item.StringContent == "x").Id;
                    emptyId = Assert.Single(input.Graph.Objects,
                        item => item.Kind == CapturedObjectKind.String && item.StringContent == string.Empty).Id;
                    Assert.NotEqual(0u, originalStringId);
                    Assert.NotEqual(0u, emptyId);
                }
                if (stage == 1) {
                    equalStringId = Assert.Single(input.Graph.Objects,
                        item => item.Kind == CapturedObjectKind.String && item.StringContent == "x" && item.Id != originalStringId).Id;
                    Assert.NotEqual(originalStringId, equalStringId);
                }
                uint name = stage < 2 ? originalStringId : equalStringId;
                uint alias = stage == 0 ? originalStringId : equalStringId;
                byte score = (byte)(Math.Min(stage, 2) + 1);
                // Four one-byte references/scalar values followed by eight fixed-width zero doubles.
                expected[stage] = new byte[68];
                expected[stage][0] = checked((byte)name);
                expected[stage][1] = checked((byte)alias);
                expected[stage][2] = checked((byte)emptyId);
                expected[stage][3] = score;
                Assert.Equal(expected[stage], owner.Base.Payload.ToArray());
                if (stage is 1 or 2) {
                    Assert.True(owner.Delta!.HasChanges);
                    Assert.Equal<byte>([stage == 1 ? (byte)0x0A : (byte)0x09, 0, checked((byte)equalStringId), score],
                        owner.Delta.Payload.ToArray());
                }
                if (stage >= 3) {
                    Assert.False(owner.Delta!.HasChanges);
                    Assert.Equal<byte>([0, 0], owner.Delta.Payload.ToArray());
                }

                FrameAddress? parent = stage == 0 ? null : revisions[stage - 1];
                IReadOnlyDictionary<uint, FrameAddress> priorHeads = parent is null
                    ? new Dictionary<uint, FrameAddress>() : store.ReadLiveObjectHeads(parent.Value);
                PreparedObject[] rows = input.Rows.Reverse().Select(row => {
                    if (!priorHeads.TryGetValue(row.Id, out FrameAddress prior)) return PreparedObject.New(row.Id, row.Base);
                    return row.IsString ? PreparedObject.Unchanged(row.Id, prior, row.Base)
                        : PreparedObject.Compared(row.Id, prior, row.Base, row.Delta!);
                }).ToArray();
                var parameters = new ReadAmplificationBaseBudgetParameters(stage < 3 ? 100 : 1, 100);
                var prepared = ObjectRevisionPlanner.PrepareRevision(store, parent, rows, parameters);
                Assert.Same(accepted, session.Current); // Planning never installs a candidate or baseline.
                Assert.Equal(parent, prepared.Revision.ParentRevisionAddress);
                if (stage == 0) {
                    Assert.Equal(input.Rows.Length, prepared.Revision.LocalObjects.Count);
                    Assert.All(prepared.Revision.LocalObjects, record => Assert.Equal(ObjectVersionKind.Base, record.Kind));
                }
                else if (stage is 1 or 2) {
                    ObjectVersionRecord record = Assert.Single(prepared.Revision.LocalObjects, item => item.ObjectId == ownerId);
                    Assert.Equal(ObjectVersionKind.Delta, record.Kind);
                    Assert.Equal(owner.Delta!.Payload.ToArray(), record.Body.ToArray());
                    Assert.Equal(priorHeads[ownerId], record.PriorAddress);
                    Assert.Equal(stage == 1 ? Array.Empty<uint>() : new[] { originalStringId }, prepared.Revision.RemovedObjectIds);
                }
                else if (stage == 3) {
                    ObjectVersionRecord record = Assert.Single(prepared.Revision.LocalObjects);
                    Assert.Equal(ownerId, record.ObjectId);
                    Assert.Equal(ObjectVersionKind.Base, record.Kind);
                    Assert.Equal(expected[stage], record.Body.ToArray());
                }
                else {
                    Assert.Empty(prepared.Revision.LocalObjects);
                    Assert.Empty(prepared.Revision.RemovedObjectIds);
                }
                revisions[stage] = store.Append(prepared.Revision);
                Assert.Same(accepted, session.Current); // Append produces an address, not a publication or Capture.Accept.
                foreach (var record in prepared.Revision.LocalObjects) {
                    var row = input.Rows.Single(item => item.Id == record.ObjectId);
                    metadata.Add((revisions[stage], record.ObjectId), new(row.IsString, row.Schema,
                        row.IsString ? "string-v1" : "generated-v1"));
                }
                var heads = store.ReadLiveObjectHeads(revisions[stage]);
                Assert.Equal(input.Rows.Select(row => row.Id).Order(), heads.Keys.Order());
                Assert.Equal(stage == 0 ? revisions[0] : priorHeads[emptyId], heads[emptyId]);
                if (stage == 1) Assert.Equal(revisions[0], heads[originalStringId]);
                if (stage >= 2) Assert.False(heads.ContainsKey(originalStringId));
                if (stage >= 2) Assert.Equal(revisions[1], heads[equalStringId]);
                ObjectVersionChain chain = store.ReadObjectVersionChain(revisions[stage], ownerId);
                if (stage == 0) initialH = chain.ReconstructionBytes;
                if (stage == 2) {
                    accumulatedH = chain.ReconstructionBytes;
                    Assert.True(accumulatedH > initialH);
                    Assert.Equal(3, chain.Records.Count);
                }
                if (stage >= 3) {
                    Assert.Single(chain.Records);
                    Assert.Equal(initialH, chain.ReconstructionBytes);
                    Assert.Equal(revisions[3], chain.HeadAddress);
                }
                if (stage < revisions.Length - 1) session.Accept(input.Graph); // Explicit fixture baseline choice only.
                else session.Discard(input.Graph);
            }
        }
        Assert.Equal(70, initialH);
        Assert.True(accumulatedH > initialH);
        Assert.Equal(revisions.Length, revisions.Select(address => address.FileNumber).Distinct().Count());

        using SegmentStore reopened = SegmentStore.OpenReadOnlyExisting(directory.Path, options);
        StateRevisionStore cold = new(reopened);
        for (int stage = 0; stage < revisions.Length; stage++) {
            var heads = cold.ReadLiveObjectHeads(revisions[stage]);
            ObjectVersionChain chain = cold.ReadObjectVersionChain(revisions[stage], ownerId);
            PreflightTypedChain(chain, metadata, schema!, "generated-v1");
            Assert.All(roots[stage].Where(id => id != 0), id => Assert.Equal(ownerId, id));
            Dictionary<uint, byte[]> strings = [];
            foreach ((uint id, FrameAddress address) in heads.Where(item => item.Key != ownerId)) {
                DeltaFixtureDescriptor descriptor = metadata[(address, id)];
                Assert.True(descriptor.IsString);
                Assert.Null(descriptor.Schema);
                Assert.Equal("string-v1", descriptor.Codec);
                strings.Add(id, cold.ReadObjectBase(revisions[stage], id));
            }
            byte[][] bodies = chain.Records.Select(entry => entry.Record.Body.ToArray()).ToArray();
            Assert.Equal(expected[stage], decode(bodies, strings, stage == 1));
            // Even references unchanged by Delta must be validated against the target view.
            strings.Remove(emptyId);
            Assert.Throws<InvalidDataException>(() => decode(bodies, strings, stage == 1));
        }
        Assert.True(cold.ReadLiveObjectHeads(revisions[0]).ContainsKey(originalStringId));
        Assert.False(cold.ReadLiveObjectHeads(revisions[2]).ContainsKey(originalStringId));
        Assert.Equal<byte>([3, (byte)'x'], cold.ReadObjectBase(revisions[0], originalStringId));
    }

    private delegate (CapturedGraph Graph,
        (uint Id, bool IsString, DurableSchema? Schema, PreparedBase Base, PreparedDelta? Delta)[] Rows)
        PreparedRevisionCapture(int stage);
    private delegate byte[] PreparedRevisionDecode(byte[][] bodies, Dictionary<uint, byte[]> strings, bool expectDistinct);

    private const string PreparedRevisionSource = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using Atelia.DurableGraph;
        using Atelia.DurableGraph.StateStore.Serialization;
        namespace PreparedRevisionWitness;
        [DurableType("prepared-revision.owner", 1, SchemaOnly = true, GenerateBinaryBody = true)]
        public sealed partial class Owner : DurableBase {
            [DurableField(1)] private string _name;
            [DurableField(2)] private string _alias;
            [DurableField(3)] private string _empty = string.Empty;
            [DurableField(4)] private uint _score;
            [DurableField(10)] private double _a;
            [DurableField(11)] private double _b;
            [DurableField(12)] private double _c;
            [DurableField(13)] private double _d;
            [DurableField(14)] private double _e;
            [DurableField(15)] private double _f;
            [DurableField(16)] private double _g;
            [DurableField(17)] private double _h;
            public Owner(string value) { _name = value; _alias = value; }
            public void Change(string name, string alias, uint score) { _name = name; _alias = alias; _score = score; }
        }
        public static class Host {
            public static readonly CaptureSession Session = new();
            private static readonly string Shared = new string(new[] { 'x' });
            private static readonly string Equal = new string(new[] { 'x' });
            private static readonly Owner Domain = new(Shared);
            public static (CapturedGraph Graph,
                (uint Id, bool IsString, DurableSchema? Schema, PreparedBase Base, PreparedDelta? Delta)[] Rows) Capture(int stage) {
                if (ReferenceEquals(Shared, Equal)) throw new Exception("Fixture requires distinct equal strings.");
                Domain.Change(stage < 2 ? Shared : Equal, stage == 0 ? Shared : Equal, (uint)(Math.Min(stage, 2) + 1));
                var context = Session.BeginCapture();
                Owner.__DurableBinaryBody.AddRoot(context, Domain);
                Owner.__DurableBinaryBody.AddRoot(context, Domain);
                Owner.__DurableBinaryBody.AddRoot(context, null);
                var graph = context.Seal();
                Domain.Change("mutated after Seal", "never captured", 99);
                var rows = graph.Objects.Select(item => {
                    if (item.Kind == CapturedObjectKind.String)
                        return (item.Id, true, (DurableSchema?)null, StringPayloadCodec.PrepareBase(item.StringContent!), (PreparedDelta?)null);
                    var current = item.GetState<Owner.__DurableBinaryBody.V1>();
                    PreparedDelta? delta = null;
                    if (Session.Current is not null) {
                        var prior = Session.Current.Objects.Single(old => old.Id == item.Id).GetState<Owner.__DurableBinaryBody.V1>();
                        delta = Owner.__DurableBinaryBody.PrepareDelta(in prior, in current);
                    }
                    return (item.Id, false, item.Schema, Owner.__DurableBinaryBody.PrepareBase(in current), delta);
                }).ToArray();
                return (graph, rows);
            }
            public static byte[] Decode(byte[][] bodies, Dictionary<uint, byte[]> stringBodies, bool expectDistinct) {
                var reader = new BinaryPayloadReader(bodies[0]);
                var state = Owner.__DurableBinaryBody.ReadV1(ref reader);
                reader.EnsureFullyConsumed();
                for (int i = 1; i < bodies.Length; i++) {
                    var deltaReader = new BinaryPayloadReader(bodies[i]);
                    state = Owner.__DurableBinaryBody.ApplyDeltaV1(ref deltaReader, in state);
                    deltaReader.EnsureFullyConsumed();
                }
                var strings = StringReadTable.Decode(stringBodies.Select(item => (item.Key, (ReadOnlyMemory<byte>)item.Value)));
                Owner.__DurableBinaryBody.ValidateStringReferences(in state, strings);
                string name = strings.ResolveString(state.Segment0Field1)!;
                string alias = strings.ResolveString(state.Segment0Field2)!;
                if (name != "x" || alias != "x" || ReferenceEquals(name, alias) == expectDistinct ||
                    !ReferenceEquals(strings.ResolveString(state.Segment0Field3), string.Empty))
                    throw new InvalidDataException("String content, sharing or Empty identity failed.");
                return Owner.__DurableBinaryBody.PrepareBase(in state).Payload.ToArray();
            }
        }
        """;
}
