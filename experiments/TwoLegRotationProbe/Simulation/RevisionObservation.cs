using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed record RevisionObservation(
    int StepIndex,
    AccountingScope AccountingScope,
    AbsoluteFrameAddress Address,
    RbfFrameLayoutEstimate ObjectPayloadOnlyLayout,
    int ObjectVersionCount,
    int CreatedObjectCount,
    int UpdatedObjectCount,
    int RemovedObjectCount,
    int LiveBindingCount,
    int BaseVersionCount,
    int DeltaVersionCount,
    long BaseObjectPayloadBytes,
    long DeltaObjectPayloadBytes,
    PostSaveReconstructionMetrics PostSaveReconstruction) {
    public int UpsertBindingCount => CreatedObjectCount + UpdatedObjectCount;

    public bool IncludesObjectVersionHeaders => false;

    public bool IncludesObjectVersionDict => false;

    public bool IncludesTailMetaIndex => false;

    public long ObjectPayloadBytesWritten =>
        BaseObjectPayloadBytes + DeltaObjectPayloadBytes;

    public int ObjectPayloadOnlyRbfFrameLengthBytes =>
        ObjectPayloadOnlyLayout.FrameLengthBytes;

    public int ObjectPayloadOnlyRbfAppendBytes =>
        ObjectPayloadOnlyLayout.AppendLengthBytes;
}
