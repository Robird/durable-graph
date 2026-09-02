using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;

namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal sealed class InMemorySegment {
    private readonly Dictionary<FrameTicket, RenderedFrameCandidate> _frames = [];

    public InMemorySegment(FileNumber fileNumber) {
        if (fileNumber.Value == 0) {
            throw new ArgumentOutOfRangeException(nameof(fileNumber));
        }

        FileNumber = fileNumber;
    }

    public FileNumber FileNumber { get; }

    public int FrameCount => _frames.Count;

    public bool IsEmpty => _frames.Count == 0;

    public long TailOffsetBytes { get; private set; } =
        ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes;

    public FrameTicket Append(RenderedFrameCandidate candidate) {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.FileNumber != FileNumber ||
            candidate.Layout.FrameStartOffsetBytes != TailOffsetBytes) {
            throw new InvalidDataException(
                "Rendered candidate does not target this Segment's exact append tail.");
        }

        ProvisionalFrameLayout independentlyEstimated =
            ProvisionalFrameEnvelopeEstimator.Estimate(
                TailOffsetBytes,
                candidate.Layout.PayloadLengthBytes,
                candidate.Layout.TailMetadataLengthBytes);
        if (independentlyEstimated != candidate.Layout ||
            candidate.Address != new AbsoluteFrameAddress(
                FileNumber,
                independentlyEstimated.Ticket)) {
            throw new InvalidDataException(
                "Rendered candidate address and sole envelope estimate disagree.");
        }

        _frames.Add(independentlyEstimated.Ticket, candidate);
        TailOffsetBytes = independentlyEstimated.TailOffsetAfterBytes;
        return independentlyEstimated.Ticket;
    }

    public RenderedFrameCandidate Read(FrameTicket ticket) =>
        _frames.TryGetValue(ticket, out RenderedFrameCandidate? frame)
            ? frame
            : throw new KeyNotFoundException(
                $"Frame {ticket} does not exist in Segment {FileNumber}.");
}
