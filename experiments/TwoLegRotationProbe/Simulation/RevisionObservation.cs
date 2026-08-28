using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Encoding;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed record RevisionObservation(
    int StepIndex,
    AccountingScope AccountingScope,
    AbsoluteFrameAddress Address,
    FrameAccountingEstimate AccountingEstimate,
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

    public RbfFrameLayoutEstimate ModeledLayout => AccountingEstimate.RbfLayout;

    public RbfFrameLayoutEstimate ObjectPayloadOnlyLayout =>
        AccountingScope == AccountingScope.ObjectPayloadOnly
            ? ModeledLayout
            : throw new InvalidOperationException(
                "ProvisionalRevisionV0 layout cannot be read through an ObjectPayloadOnly property.");

    public ProvisionalRevisionV0Estimate? ProvisionalRevisionV0 =>
        AccountingEstimate.ProvisionalRevisionV0;

    public bool IncludesObjectVersionHeaders =>
        AccountingScope == AccountingScope.ProvisionalRevisionV0;

    public bool IncludesObjectVersionDict =>
        AccountingScope == AccountingScope.ProvisionalRevisionV0;

    public bool IncludesTailMetaIndex =>
        AccountingScope == AccountingScope.ProvisionalRevisionV0;

    public long ObjectPayloadBytesWritten =>
        BaseObjectPayloadBytes + DeltaObjectPayloadBytes;

    public int ModeledRbfFrameLengthBytes => ModeledLayout.FrameLengthBytes;

    public int ModeledRbfAppendBytes => ModeledLayout.AppendLengthBytes;

    public int ObjectPayloadOnlyRbfFrameLengthBytes =>
        ObjectPayloadOnlyLayout.FrameLengthBytes;

    public int ObjectPayloadOnlyRbfAppendBytes =>
        ObjectPayloadOnlyLayout.AppendLengthBytes;
}
