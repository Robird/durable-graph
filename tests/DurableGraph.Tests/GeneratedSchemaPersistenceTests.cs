using Atelia.DurableGraph.StateStore;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedSchemasRemainDurableAfterDefiniteStateAppendFailure() {
        GeneratorTestRun run = RunGenerator(PreparedRevisionSource);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("PreparedRevisionWitness.Host")!;
        var capture = host.GetMethod("Capture")!.CreateDelegate<PreparedRevisionCapture>();
        var session = (CaptureSession)host.GetField("Session")!.GetValue(null)!;
        CapturedGraph candidate = capture(1);
        PreparedCapturedGraph input = session.Prepare(candidate);

        using RawBaseDirectory directory = new();
        using RawBaseDirectory schemaDirectory = new();
        Directory.CreateDirectory(schemaDirectory.Path);
        string schemaPath = Path.Combine(schemaDirectory.Path, "schemas.rbf");
        using (SegmentStore created = SegmentStore.CreateNew(directory.Path)) { }
        using (SegmentStore segments = SegmentStore.OpenReadOnlyExisting(directory.Path))
        using (var schemaFile = RbfFile.CreateNew(schemaPath)) {
            SchemaStore schemas = new(schemaFile);
            StateRevisionStore store = new(segments);
            var stateTail = ReadActiveTail(segments);
            long emptySchemaTail = schemaFile.TailOffset;
            var plan = CapturedRevisionPlanner.PrepareRevision(store, schemas, null, input, new(100, 100));
            Assert.Equal(3, schemas.Count); // Owner, Tag and Tag's exact ancestor.
            Assert.True(schemaFile.TailOffset > emptySchemaTail);
            Assert.Throws<InvalidOperationException>(() => store.Append(plan.Revision));
            Assert.Equal(stateTail, ReadActiveTail(segments));
            Assert.Null(session.Current);
            Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
            session.Discard(candidate);
        }
        using var reopenedFile = RbfFile.OpenReadOnlyExisting(schemaPath);
        SchemaStore reopened = new(reopenedFile, readOnly: true);
        Assert.Equal(3, reopened.Count);
        foreach (var row in input.Objects.Where(item => item.Current.Kind == ObjectStateKind.Durable)) {
            DurableSchema expected = row.Current.Schema!;
            Assert.Equal(expected, reopened.GetRequired(expected.SchemaId, expected.Version));
            if (expected.BaseSchema is { } ancestor) {
                Assert.Equal(ancestor, reopened.GetRequired(ancestor.SchemaId, ancestor.Version));
            }
        }
        Assert.Null(session.Current);
    }

    [Fact]
    public void GeneratedSchemaConflictRejectsEntireBatchBeforeStateAppendOrCandidateInstall() {
        GeneratorTestRun run = RunGenerator(PreparedRevisionSource);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("PreparedRevisionWitness.Host")!;
        var capture = host.GetMethod("Capture")!.CreateDelegate<PreparedRevisionCapture>();
        var session = (CaptureSession)host.GetField("Session")!.GetValue(null)!;
        CapturedGraph candidate = capture(1); // Two concrete types, one with an exact ancestor.
        PreparedCapturedGraph input = session.Prepare(candidate);
        DurableSchema tag = input.Objects.Single(item => item.Current.Id == candidate.RootIds[3]).Current.Schema!;

        using RawBaseDirectory directory = new();
        using RawBaseDirectory schemaDirectory = new();
        Directory.CreateDirectory(schemaDirectory.Path);
        using var schemaFile = RbfFile.CreateNew(Path.Combine(schemaDirectory.Path, "schemas.rbf"));
        SchemaStore schemas = new(schemaFile);
        schemas.RegisterBatch([new DurableSchema(tag.SchemaId, tag.Version, [], tag.BaseSchema)]);
        long schemaTail = schemaFile.TailOffset;
        int registered = schemas.Count;
        using SegmentStore segments = SegmentStore.CreateNew(directory.Path);
        StateRevisionStore store = new(segments);
        var before = ReadActiveTail(segments);

        Assert.Throws<SchemaConflictException>(() => {
            var plan = CapturedRevisionPlanner.PrepareRevision(store, schemas, null, input, new(100, 100));
            store.Append(plan.Revision);
            session.Accept(candidate);
        });
        Assert.Equal(before, ReadActiveTail(segments));
        Assert.Equal(schemaTail, schemaFile.TailOffset);
        Assert.Equal(registered, schemas.Count);
        Assert.Null(session.Current);
        Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
        Assert.Throws<SchemaNotFoundException>(() => schemas.GetRequired("prepared-revision.owner", 1));
        // Rejection leaves the pending frozen candidate usable and explicitly discardable.
        Assert.Same(candidate, session.Prepare(candidate).Candidate);
        session.Discard(candidate);
    }

    [Theory]
    [InlineData("new-with-parent")]
    [InlineData("existing-without-parent")]
    [InlineData("membership")]
    [InlineData("kind")]
    [InlineData("schema")]
    public void GeneratedPreparationRejectsWrongParentBeforeRegistrationAndPreservesAcceptedBaseline(string mismatch) {
        GeneratorTestRun run = RunGenerator(PreparedRevisionSource);
        AssertSchemaOnlyCompiles(run);
        Type host = EmitAndLoad(run.OutputCompilation).GetType("PreparedRevisionWitness.Host")!;
        var capture = host.GetMethod("Capture")!.CreateDelegate<PreparedRevisionCapture>();
        var session = (CaptureSession)host.GetField("Session")!.GetValue(null)!;
        CapturedGraph first = capture(0);
        PreparedCapturedGraph initial = session.Prepare(first);
        uint ownerId = first.RootIds[0];

        using RawBaseDirectory directory = new();
        using RawBaseDirectory schemaDirectory = new();
        Directory.CreateDirectory(schemaDirectory.Path);
        using var schemaFile = RbfFile.CreateNew(Path.Combine(schemaDirectory.Path, "schemas.rbf"));
        SchemaStore schemas = new(schemaFile);
        using SegmentStore segments = SegmentStore.CreateNew(directory.Path);
        StateRevisionStore store = new(segments);
        var firstPlan = CapturedRevisionPlanner.PrepareRevision(store, schemas, null, initial, new(100, 100));
        FrameAddress parent = store.Append(firstPlan.Revision);
        session.Accept(first);
        CapturedGraph candidate = capture(0);
        PreparedCapturedGraph input = session.Prepare(candidate);
        Assert.False(input.Objects.Single(item => item.Current.Id == ownerId).DeltaBody!.HasChanges);

        FrameAddress? selectedParent = parent;
        switch (mismatch) {
            case "new-with-parent":
                input = initial;
                break;
            case "existing-without-parent":
                selectedParent = null;
                break;
            case "membership":
                selectedParent = store.Append(StateRevision.CreateDelta(parent, [],
                    [first.Objects.First(item => item.Kind == ObjectStateKind.String).Id]));
                break;
            case "kind":
                var text = BaseObjectBodyCodec.EncodeString(StringPayloadCodec.PrepareBase("x"));
                selectedParent = store.Append(StateRevision.CreateDelta(parent,
                    [ObjectVersionRecord.CreateBase(ownerId, text.Body)], []));
                break;
            case "schema":
                var owner = initial.Objects.Single(item => item.Current.Id == ownerId);
                var original = owner.Current.Schema!;
                var versionTwo = new DurableSchema(original.SchemaId, 2, original.Fields.ToArray(), original.BaseSchema);
                schemas.RegisterBatch([versionTwo]);
                var differentType = BaseObjectBodyCodec.EncodeDurable(versionTwo, owner.BaseBody);
                selectedParent = store.Append(StateRevision.CreateDelta(parent,
                    [ObjectVersionRecord.CreateBase(ownerId, differentType.Body)], []));
                break;
        }
        long schemaTail = schemaFile.TailOffset;
        var stateTail = ReadActiveTail(segments);
        Exception error = Assert.ThrowsAny<Exception>(() => {
            var plan = CapturedRevisionPlanner.PrepareRevision(store, schemas, selectedParent, input, new(1, 100));
            store.Append(plan.Revision);
            session.Accept(candidate);
        });
        if (mismatch is "kind" or "schema") Assert.IsType<InvalidDataException>(error);
        else Assert.IsType<ArgumentException>(error);
        Assert.Equal(stateTail, ReadActiveTail(segments));
        Assert.Equal(schemaTail, schemaFile.TailOffset);
        Assert.Same(first, session.Current);
        Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
        Assert.Same(candidate, session.Prepare(candidate).Candidate);

        // A valid retry against the exact original parent has no writes, including
        // no redundant registration, and still does not install the candidate.
        var retry = CapturedRevisionPlanner.PrepareRevision(store, schemas, parent, session.Prepare(candidate), new(100, 100));
        Assert.Empty(retry.Revision.LocalObjects);
        Assert.Equal(schemaTail, schemaFile.TailOffset);
        Assert.Same(first, session.Current);
        session.Discard(candidate);
    }

    private static (uint Segment, long Tail) ReadActiveTail(SegmentStore segments) {
        using var lease = segments.OpenReader(segments.ActiveSegmentNumber);
        return (lease.SegmentNumber, lease.File.TailOffset);
    }
}
