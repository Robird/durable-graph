using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Planning;

namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal sealed class InMemorySegmentStore {
    private readonly List<InMemorySegment> _segments = [];
    private readonly FileNumber _initialFileNumber;

    public InMemorySegmentStore()
        : this(new FileNumber(1)) {
    }

    /// <summary>
    /// Allows a bounded probe fixture to start at an already-established file number.
    /// It does not imply a catalog or permission to reuse a published number.
    /// </summary>
    internal InMemorySegmentStore(FileNumber initialFileNumber) {
        if (initialFileNumber.Value == 0) {
            throw new ArgumentOutOfRangeException(nameof(initialFileNumber));
        }

        _initialFileNumber = initialFileNumber;
    }

    public int SegmentCount => _segments.Count;

    public IReadOnlyList<InMemorySegment> Segments =>
        new ReadOnlyCollection<InMemorySegment>(_segments);

    public InMemorySegment? CurrentSegment => _segments.Count == 0
        ? null
        : _segments[^1];

    public FileNumber AppendFileNumber => CurrentSegment?.FileNumber ??
        _initialFileNumber;

    public InMemorySegment GetSegment(FileNumber fileNumber) =>
        _segments.FirstOrDefault(segment => segment.FileNumber == fileNumber)
        ?? throw new KeyNotFoundException(
            $"Segment {fileNumber} does not exist.");

    public RenderedFrameCandidate Read(AbsoluteFrameAddress address) =>
        GetSegment(address.FileNumber).Read(address.FrameTicket);

    /// <summary>
    /// Models an already-created append destination that contains no complete Frame.
    /// Normal commits still create a Segment only together with their first admitted Frame.
    /// </summary>
    internal InMemorySegment CreateEmptyAppendSegment() {
        InMemorySegment? current = CurrentSegment;
        if (current is { IsEmpty: true }) {
            return current;
        }

        FileNumber fileNumber = current?.FileNumber.Next() ?? _initialFileNumber;
        InMemorySegment empty = new(fileNumber);
        _segments.Add(empty);
        return empty;
    }

    public void Append(RenderedFrameCandidate candidate) {
        ArgumentNullException.ThrowIfNull(candidate);
        InMemorySegment? current = CurrentSegment;
        if (current is null) {
            if (candidate.FileNumber != _initialFileNumber ||
                candidate.Layout.FrameStartOffsetBytes !=
                    ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes) {
                throw new InvalidDataException(
                    "The first candidate must target the configured initial Segment.");
            }

            AppendToFreshSegment(candidate);
            return;
        }

        if (candidate.FileNumber == current.FileNumber) {
            _ = current.Append(candidate);
            return;
        }

        FileNumber expectedNext;
        try {
            expectedNext = current.FileNumber.Next();
        } catch (OverflowException exception) {
            throw new InvalidDataException(
                "No file number remains after the current Segment.",
                exception);
        }

        if (candidate.FileNumber != expectedNext ||
            candidate.Layout.FrameStartOffsetBytes !=
                ProvisionalFrameEnvelopeEstimator.InitialTailOffsetBytes) {
            throw new InvalidDataException(
                "A fresh candidate must target exactly the next Segment's initial tail.");
        }

        AppendToFreshSegment(candidate);
    }

    private void AppendToFreshSegment(RenderedFrameCandidate candidate) {
        InMemorySegment fresh = new(candidate.FileNumber);
        _ = fresh.Append(candidate);
        _segments.Add(fresh);
    }
}
