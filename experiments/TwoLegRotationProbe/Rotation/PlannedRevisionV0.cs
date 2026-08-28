using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

internal enum PlannedRevisionV0Role {
    Relay,
    Evacuation,
}

/// <summary>
/// Pure, grammar-level description of one revision that a rotation would append.
/// It is not a publication command and does not own mutable store state.
/// </summary>
internal sealed class PlannedRevisionV0 {
    public PlannedRevisionV0(
        PlannedRevisionV0Role role,
        uint fileNumber,
        AbsoluteFrameAddress address,
        ProvisionalRevisionV0Input grammarInput,
        ProvisionalRevisionV0Estimate estimate) {
        if (!Enum.IsDefined(role)) {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

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

        Role = role;
        FileNumber = fileNumber;
        Address = address;
        GrammarInput = grammarInput;
        Estimate = estimate;
    }

    public PlannedRevisionV0Role Role { get; }

    public uint FileNumber { get; }

    public AbsoluteFrameAddress Address { get; }

    public ProvisionalRevisionV0Input GrammarInput { get; }

    public ProvisionalRevisionV0Estimate Estimate { get; }
}
