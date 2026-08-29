using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ImmediateRotationPlannerLineageIsolationTests {
    private const uint ObjectId = 1;

    [Fact]
    public void Broken_historical_Base_locator_does_not_block_current_state_rotation() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress unreadableLocator = Append(previous, new FrameBuilder());

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new();
        currentDictionary.BindSelf(ObjectId);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        ObjectVersionBuilder currentVersion = currentBuilder.Add(ObjectId);
        currentVersion.Kind = ObjectVersionKind.Base;
        currentVersion.PayloadBytes = 10;
        currentVersion.ReconstructionObjectPayloadBytes = 10;
        currentVersion.ResultBasePayloadBytes = 10;
        currentVersion.LogicalVersionOrdinal = 2;
        currentVersion.ParentFrameTicket = new RelativeFrameTicket(
            IsPreviousFile: true,
            unreadableLocator.FrameTicket);
        AbsoluteFrameAddress publishedRevision = Append(current, currentBuilder);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                publishedRevision);
        Assert.Equal(new LogicalObjectState(10, 2), reconstruction.State);
        Assert.Equal([publishedRevision], reconstruction.ReconstructionFrameAddresses);
        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                store,
                ObjectId,
                publishedRevision));

        ImmediateRotationPlan plan = ImmediateRotationPlanner.Create(
            store,
            current.FileNumber,
            publishedRevision);

        Assert.Empty(plan.EvacuationObjectIds);
        Assert.Empty(plan.EvacuationRevision.Frame.ObjectVersions);
        ObjectVersionDictionary plannedOvd = Assert.IsType<ObjectVersionDictionary>(
            plan.EvacuationRevision.Frame.ObjectVersionDictionary);
        ObjectVersionDictionaryBinding retained = plannedOvd.Entries[ObjectId];
        Assert.Equal(ObjectVersionDictionaryBindingKind.External, retained.Kind);
        Assert.Equal(
            publishedRevision,
            new FileScope(plan.NextFileNumber).Resolve(
                Assert.IsType<RelativeFrameTicket>(retained.ExternalFrameTicket)));

        AbsoluteFrameAddress appended = ImmediateRotationAppender.AppendToFreshNextFile(
            store,
            plan);
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> currentStateMap =
            ObjectVersionDictionaryReader.MaterializeLive(store, appended).Bindings;
        Assert.Equal(publishedRevision, currentStateMap[ObjectId]);
        Assert.Equal(
            new LogicalObjectState(10, 2),
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                ObjectId,
                currentStateMap[ObjectId]).State);
        Assert.Throws<InvalidDataException>(() =>
            PhysicalStateOracle.InspectObjectLineage(
                store,
                ObjectId,
                currentStateMap[ObjectId]));
    }

    private static AbsoluteFrameAddress Append(RbfFile file, FrameBuilder builder) =>
        new(file.FileNumber, file.Append(builder.Build()));
}
