using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Pure, grammar-level description of one revision that a rotation would append.
/// It is not a publication command and does not own mutable store state.
/// </summary>
internal sealed class PlannedRevisionV0 {
    public PlannedRevisionV0(
        uint fileNumber,
        AbsoluteFrameAddress address,
        ProvisionalRevisionV0Input grammarInput,
        ProvisionalRevisionV0Estimate estimate) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        ArgumentNullException.ThrowIfNull(grammarInput);
        ArgumentNullException.ThrowIfNull(estimate);
        if (address.FileNumber != fileNumber) {
            throw new ArgumentException(
                "The planned address must belong to the planned file.",
                nameof(address));
        }

        if (address.FrameTicket != estimate.RbfLayout.Ticket) {
            throw new ArgumentException(
                "The planned address must match the estimated RBF ticket.",
                nameof(address));
        }

        FileNumber = fileNumber;
        Address = address;
        GrammarInput = grammarInput;
        Estimate = estimate;
    }

    public uint FileNumber { get; }

    public AbsoluteFrameAddress Address { get; }

    public ProvisionalRevisionV0Input GrammarInput { get; }

    public ProvisionalRevisionV0Estimate Estimate { get; }
}
