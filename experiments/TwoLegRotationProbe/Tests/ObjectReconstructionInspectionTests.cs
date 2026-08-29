using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ObjectReconstructionInspectionTests {
    [Fact]
    public void Base_inspection_reports_the_head_as_the_terminating_base() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket baseTicket = AppendBase(file, objectId: 7, resultBasePayloadBytes: 30);
        AbsoluteFrameAddress head = new(file.FileNumber, baseTicket);

        ObjectReconstructionInspection inspection =
            PhysicalStateOracle.InspectObjectReconstruction(store, 7, head);

        Assert.Equal(new LogicalObjectState(30, 1), inspection.State);
        Assert.Equal(head, inspection.HeadAddress);
        Assert.Equal(head, inspection.BaseAddress);
        Assert.Equal([head], inspection.ReconstructionFrameAddresses);
        Assert.IsAssignableFrom<IReadOnlyList<AbsoluteFrameAddress>>(
            inspection.ReconstructionFrameAddresses);
        Assert.True(
            ((IList<AbsoluteFrameAddress>)inspection.ReconstructionFrameAddresses).IsReadOnly);
    }

    [Fact]
    public void Delta_inspection_reports_head_to_base_reconstruction_frames() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket baseTicket = AppendBase(file, objectId: 7, resultBasePayloadBytes: 30);
        FrameTicket firstDeltaTicket = AppendDelta(
            file,
            objectId: 7,
            parentTicket: baseTicket,
            parentBasePayloadBytes: 30,
            resultBasePayloadBytes: 40,
            payloadBytes: 10,
            reconstructionPayloadBytes: 40,
            logicalVersionOrdinal: 2);
        FrameTicket secondDeltaTicket = AppendDelta(
            file,
            objectId: 7,
            parentTicket: firstDeltaTicket,
            parentBasePayloadBytes: 40,
            resultBasePayloadBytes: 35,
            payloadBytes: 3,
            reconstructionPayloadBytes: 43,
            logicalVersionOrdinal: 3);
        AbsoluteFrameAddress head = new(file.FileNumber, secondDeltaTicket);
        AbsoluteFrameAddress firstDelta = new(file.FileNumber, firstDeltaTicket);
        AbsoluteFrameAddress @base = new(file.FileNumber, baseTicket);

        ObjectReconstructionInspection inspection =
            PhysicalStateOracle.InspectObjectReconstruction(store, 7, head);

        Assert.Equal(new LogicalObjectState(35, 3), inspection.State);
        Assert.Equal(head, inspection.HeadAddress);
        Assert.Equal(@base, inspection.BaseAddress);
        Assert.Equal(
            [head, firstDelta, @base],
            inspection.ReconstructionFrameAddresses);
    }

    [Fact]
    public void Inspection_reuses_fail_closed_reconstruction_validation() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket baseTicket = AppendBase(file, objectId: 7, resultBasePayloadBytes: 30);
        FrameTicket badDeltaTicket = AppendDelta(
            file,
            objectId: 7,
            parentTicket: baseTicket,
            parentBasePayloadBytes: 29,
            resultBasePayloadBytes: 40,
            payloadBytes: 10,
            reconstructionPayloadBytes: 40,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress head = new(file.FileNumber, badDeltaTicket);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.InspectObjectReconstruction(store, 7, head));

        Assert.Contains("expected a 29-byte parent", exception.Message);
    }

    [Fact]
    public void Inspection_rejects_a_missing_head_record() {
        RbfFileStore store = new();
        RbfFile file = store.CreateFile();
        FrameTicket baseTicket = AppendBase(file, objectId: 8, resultBasePayloadBytes: 30);
        AbsoluteFrameAddress head = new(file.FileNumber, baseTicket);

        Assert.Throws<InvalidDataException>(
            () => PhysicalStateOracle.InspectObjectReconstruction(store, 7, head));
    }

    private static FrameTicket AppendBase(
        RbfFile file,
        uint objectId,
        int resultBasePayloadBytes) {
        FrameBuilder frame = new();
        ObjectVersionBuilder builder = frame.Add(objectId);
        builder.Kind = ObjectVersionKind.Base;
        builder.PayloadBytes = resultBasePayloadBytes;
        builder.ReconstructionObjectPayloadBytes = resultBasePayloadBytes;
        builder.ResultBasePayloadBytes = resultBasePayloadBytes;
        builder.LogicalVersionOrdinal = 1;
        return file.Append(frame.Build());
    }

    private static FrameTicket AppendDelta(
        RbfFile file,
        uint objectId,
        FrameTicket parentTicket,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int payloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        FrameBuilder frame = new();
        ObjectVersionBuilder builder = frame.Add(objectId);
        builder.Kind = ObjectVersionKind.Delta;
        builder.PayloadBytes = payloadBytes;
        builder.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        builder.ResultBasePayloadBytes = resultBasePayloadBytes;
        builder.ExpectedParentBasePayloadBytes = parentBasePayloadBytes;
        builder.LogicalVersionOrdinal = logicalVersionOrdinal;
        builder.ParentFrameTicket = new RelativeFrameTicket(false, parentTicket);
        return file.Append(frame.Build());
    }
}
