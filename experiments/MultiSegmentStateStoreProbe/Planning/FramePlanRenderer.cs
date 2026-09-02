using Atelia.MultiSegmentStateStoreProbe.Encoding;
using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

internal static class FramePlanRenderer {
    public static RenderedFrameCandidate RenderAndMeasure(
        OriginFreeFramePlan plan,
        FileNumber targetFileNumber,
        long frameStartOffsetBytes) {
        ArgumentNullException.ThrowIfNull(plan);
        FileScope scope = new(targetFileNumber);
        RelativeFrameTicket[] relativeReferences = plan.ExternalReferences
            .Select(scope.Relativize)
            .ToArray();
        int encodedLength = relativeReferences.Aggregate(
            0,
            static (total, reference) => checked(
                total + RelativeFrameTicketWireCodec.GetEncodedLength(reference)));
        byte[] encodedReferences = new byte[encodedLength];
        int cursor = 0;
        foreach (RelativeFrameTicket reference in relativeReferences) {
            cursor += RelativeFrameTicketWireCodec.Write(
                encodedReferences.AsSpan(cursor),
                reference);
        }

        long payloadLengthBytes = checked(
            (long)plan.SyntheticPayloadBytes + encodedReferences.Length);
        ProvisionalFrameLayout layout = ProvisionalFrameEnvelopeEstimator.Estimate(
            frameStartOffsetBytes,
            payloadLengthBytes,
            tailMetadataLengthBytes: 0);
        AbsoluteFrameAddress containing = new(targetFileNumber, layout.Ticket);
        foreach (AbsoluteFrameAddress target in plan.ExternalReferences) {
            FrameReferenceValidator.EnsureStrictlyEarlier(containing, target);
        }

        RevisionFrame? revisionFrame = plan.RevisionFrame?.Render(scope);

        return new RenderedFrameCandidate(
            plan,
            targetFileNumber,
            relativeReferences,
            encodedReferences,
            layout,
            revisionFrame);
    }
}
