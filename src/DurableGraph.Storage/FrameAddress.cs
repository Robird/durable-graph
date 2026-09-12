using Atelia.Data;

namespace Atelia.DurableGraph.Storage;

/// <summary>
/// Identifies one RBF Frame by its absolute file number and file-local ticket.
/// </summary>
/// <remarks>
/// Runtime state keeps this absolute representation. Backward file distance is a
/// wire-only encoding concern and is converted through <see cref="FileScope"/>.
/// </remarks>
public readonly record struct FrameAddress {
    public FrameAddress(uint fileNumber, SizedPtr frameTicket) {
        ArgumentOutOfRangeException.ThrowIfZero(fileNumber);
        if (frameTicket.Length == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(frameTicket),
                frameTicket,
                "A FrameAddress requires a non-empty SizedPtr.");
        }

        FileNumber = fileNumber;
        FrameTicket = frameTicket;
    }

    public uint FileNumber { get; }

    public SizedPtr FrameTicket { get; }
}
