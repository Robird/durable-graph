using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ReadAmplificationBaseBudgetPolicyProjectionTests {
    private const uint InsertZeroId = 5;
    private const uint AUpdateId = 10;
    private const uint ANoChangeZeroId = 20;
    private const uint RemoveId = 30;
    private const uint BNoChangeId = 40;
    private const uint BUpdateZeroId = 50;
    private const uint InsertId = 25;

    [Fact]
    public void Parameters_preserve_valid_decimal_boundaries() {
        ReadAmplificationBaseBudgetPolicyParameters minimum = new(1m, 0.01m);
        ReadAmplificationBaseBudgetPolicyParameters maximum = new(2.75m, 1m);

        Assert.Equal(1m, minimum.ReadAmplificationLimit);
        Assert.Equal(0.01m, minimum.BaseBudgetFraction);
        Assert.Equal(2.75m, maximum.ReadAmplificationLimit);
        Assert.Equal(1m, maximum.BaseBudgetFraction);
    }

    [Fact]
    public void Invalid_parameters_fail_closed() {
        Assert.Equal(
            "readAmplificationLimit",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReadAmplificationBaseBudgetPolicyParameters(0.99m, 0.5m))
                .ParamName);
        Assert.Equal(
            "baseBudgetFraction",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReadAmplificationBaseBudgetPolicyParameters(1m, 0m))
                .ParamName);
        Assert.Equal(
            "baseBudgetFraction",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReadAmplificationBaseBudgetPolicyParameters(1m, -0.1m))
                .ParamName);
        Assert.Equal(
            "baseBudgetFraction",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReadAmplificationBaseBudgetPolicyParameters(1m, 1.01m))
                .ParamName);
    }

    [Fact]
    public void Executable_facts_project_canonical_post_live_payload_proxies() {
        ProjectionFixture fixture = CreateExecutableFixture();
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            fixture.Store,
            fixture.Current.FileNumber,
            fixture.PublishedRevision,
            new SaveStep([
                new UpdateObject(BUpdateZeroId, ResultBasePayloadBytes: 0,
                    DeltaPayloadBytes: 1),
                new CreateObject(InsertId, BasePayloadBytes: 6),
                new RemoveObject(RemoveId),
                new UpdateObject(AUpdateId, ResultBasePayloadBytes: 3,
                    DeltaPayloadBytes: 1),
                new CreateObject(InsertZeroId, BasePayloadBytes: 0),
            ]));

        ReadAmplificationBaseBudgetPolicyProjection projection =
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts);

        Assert.Same(facts, projection.Facts);
        Assert.Equal(17, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(3, projection.ADependentEvacuationBasePayloadBytes);
        Assert.Equal(
            [InsertZeroId, AUpdateId, ANoChangeZeroId, InsertId, BNoChangeId,
                BUpdateZeroId],
            projection.PostLiveObjects.Select(static fact => fact.ObjectId));
        Assert.DoesNotContain(
            projection.PostLiveObjects,
            static fact => fact.ObjectId == RemoveId);

        AssertObject(
            projection.PostLiveObjects[0],
            ReadAmplificationBaseBudgetPolicyObjectKind.Insert,
            isADependent: false,
            basePayloadBytes: 0,
            sourceReconstructionBytes: 0,
            deltaPayloadBytes: null,
            numeratorBytes: null);
        AssertObject(
            projection.PostLiveObjects[1],
            ReadAmplificationBaseBudgetPolicyObjectKind.Update,
            isADependent: true,
            basePayloadBytes: 3,
            sourceReconstructionBytes: 14,
            deltaPayloadBytes: 1,
            numeratorBytes: 15);
        Assert.NotEqual(
            facts.ParentLive[AUpdateId].State.BasePayloadBytes,
            projection.PostLiveObjects[1].PostSaveBasePayloadBytes);
        AssertObject(
            projection.PostLiveObjects[2],
            ReadAmplificationBaseBudgetPolicyObjectKind.NoChange,
            isADependent: true,
            basePayloadBytes: 0,
            sourceReconstructionBytes: 0,
            deltaPayloadBytes: null,
            numeratorBytes: 0);
        AssertObject(
            projection.PostLiveObjects[3],
            ReadAmplificationBaseBudgetPolicyObjectKind.Insert,
            isADependent: false,
            basePayloadBytes: 6,
            sourceReconstructionBytes: 0,
            deltaPayloadBytes: null,
            numeratorBytes: null);
        AssertObject(
            projection.PostLiveObjects[4],
            ReadAmplificationBaseBudgetPolicyObjectKind.NoChange,
            isADependent: false,
            basePayloadBytes: 8,
            sourceReconstructionBytes: 8,
            deltaPayloadBytes: null,
            numeratorBytes: 8);
        AssertObject(
            projection.PostLiveObjects[5],
            ReadAmplificationBaseBudgetPolicyObjectKind.Update,
            isADependent: false,
            basePayloadBytes: 0,
            sourceReconstructionBytes: 7,
            deltaPayloadBytes: 1,
            numeratorBytes: 8);
    }

    [Fact]
    public void Source_Base_outside_the_normalized_scope_fails_closed() {
        FrameTicket ticket = new(
            RbfV040Layout.HeaderFenceBytes,
            RbfV040Layout.MinFrameLengthBytes);
        AbsoluteFrameAddress head = new(fileNumber: 2, ticket);
        AbsoluteFrameAddress outsideBase = new(fileNumber: 3, ticket);
        SourceObjectFact source = new(
            objectId: 1,
            new LogicalObjectState(BasePayloadBytes: 1, LogicalVersionOrdinal: 1),
            head,
            outsideBase,
            headReconstructionObjectPayloadBytes: 1,
            [head, outsideBase]);
        NormalizedSaveFact noChange = new NormalizedNoChangeFact(source);
        NormalizedSaveFacts facts = new(
            previousFileNumber: 1,
            currentFileNumber: 2,
            publishedRevisionAddress: head,
            parentLive: [source],
            allFacts: [noChange]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ReadAmplificationBaseBudgetPolicyProjection.Create(facts));

        Assert.Contains("outside source files 1/2", exception.Message,
            StringComparison.Ordinal);
    }

    private static ProjectionFixture CreateExecutableFixture() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, AUpdateId, payloadBytes: 10);
        AddBase(previousBuilder, ANoChangeZeroId, payloadBytes: 0);
        AddBase(previousBuilder, RemoveId, payloadBytes: 4);
        previousDictionary.BindSelf(AUpdateId);
        previousDictionary.BindSelf(ANoChangeZeroId);
        previousDictionary.BindSelf(RemoveId);
        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder publishedDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
        };
        publishedDictionary.BindSelf(AUpdateId);
        publishedDictionary.BindSelf(BNoChangeId);
        publishedDictionary.BindSelf(BUpdateZeroId);
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = publishedDictionary,
        };
        AddDelta(
            publishedBuilder,
            AUpdateId,
            Previous(previousRevision.FrameTicket),
            parentBasePayloadBytes: 10,
            resultBasePayloadBytes: 12,
            deltaPayloadBytes: 4,
            reconstructionPayloadBytes: 14,
            logicalVersionOrdinal: 2);
        AddBase(publishedBuilder, BNoChangeId, payloadBytes: 8);
        AddBase(publishedBuilder, BUpdateZeroId, payloadBytes: 7);
        AbsoluteFrameAddress publishedRevision = AppendExact(
            current,
            publishedBuilder);
        return new ProjectionFixture(store, current, publishedRevision);
    }

    private static void AssertObject(
        ReadAmplificationBaseBudgetPolicyObjectFact fact,
        ReadAmplificationBaseBudgetPolicyObjectKind kind,
        bool isADependent,
        int basePayloadBytes,
        long sourceReconstructionBytes,
        int? deltaPayloadBytes,
        long? numeratorBytes) {
        Assert.Equal(kind, fact.Kind);
        Assert.Equal(isADependent, fact.IsADependent);
        Assert.Equal(basePayloadBytes, fact.PostSaveBasePayloadBytes);
        Assert.Equal(
            sourceReconstructionBytes,
            fact.SourceHeadReconstructionObjectPayloadBytes);
        Assert.Equal(deltaPayloadBytes, fact.DeltaPayloadBytes);
        Assert.Equal(numeratorBytes, fact.ReadAmplificationNumeratorBytes);
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) => RevisionCandidateRecordBuilder.AddBase(
            frame,
            objectId,
            new LogicalObjectState(payloadBytes, LogicalVersionOrdinal: 1));

    private static void AddDelta(
        FrameBuilder frame,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = deltaPayloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = parentBasePayloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.DeltaParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress AppendExact(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        Assert.Equal(estimate.RbfLayout.Ticket, ticket);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private sealed record ProjectionFixture(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevision);
}
