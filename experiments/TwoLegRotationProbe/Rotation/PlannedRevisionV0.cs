using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Pure, immutable runtime Revision candidate plus its derived provisional size estimate.
/// It is not an append or publication command and does not own mutable store state.
/// </summary>
internal sealed class PlannedRevisionV0 {
    public PlannedRevisionV0(
        uint fileNumber,
        Frame frame,
        long frameStartOffsetBytes) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        ArgumentNullException.ThrowIfNull(frame);

        FileNumber = fileNumber;
        Frame = frame;
        Estimate = ProvisionalRevisionV0Estimator.Estimate(
            frame,
            frameStartOffsetBytes);
        Address = new AbsoluteFrameAddress(fileNumber, Estimate.RbfLayout.Ticket);
    }

    public uint FileNumber { get; }

    public AbsoluteFrameAddress Address { get; }

    public Frame Frame { get; }

    public ProvisionalRevisionV0Estimate Estimate { get; }
}
