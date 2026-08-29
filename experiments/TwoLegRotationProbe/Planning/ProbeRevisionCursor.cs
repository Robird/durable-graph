using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Caller-owned, volatile cursor for the single-threaded probe runner. It is not stored,
/// crash-safe, or a durable publication/head contract.
/// </summary>
internal sealed class ProbeRevisionCursor {
    public ProbeRevisionCursor(
        FileScope fileScope,
        AbsoluteFrameAddress publishedRevisionAddress,
        long currentFileTailOffsetBytes) {
        ArgumentNullException.ThrowIfNull(fileScope);
        if (publishedRevisionAddress.FileNumber != fileScope.CurrentFileNumber) {
            throw new ArgumentException(
                "The PublishedRevision must be in the cursor's Current file.",
                nameof(publishedRevisionAddress));
        }

        if (currentFileTailOffsetBytes < RbfV040Layout.InitialTailOffsetBytes ||
            (currentFileTailOffsetBytes & RbfV040Layout.AlignmentMask) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(currentFileTailOffsetBytes),
                currentFileTailOffsetBytes,
                "The Current-file tail must be at least the header fence and 4B-aligned.");
        }

        long publishedAppendEnd = checked(
            publishedRevisionAddress.FrameTicket.EndOffsetExclusive +
            RbfV040Layout.TrailingFenceBytes);
        if (currentFileTailOffsetBytes < publishedAppendEnd) {
            throw new ArgumentException(
                "The Current-file tail cannot precede the PublishedRevision append end.",
                nameof(currentFileTailOffsetBytes));
        }

        FileScope = fileScope;
        PublishedRevisionAddress = publishedRevisionAddress;
        CurrentFileTailOffsetBytes = currentFileTailOffsetBytes;
    }

    public FileScope FileScope { get; }

    public AbsoluteFrameAddress PublishedRevisionAddress { get; }

    public long CurrentFileTailOffsetBytes { get; }
}
