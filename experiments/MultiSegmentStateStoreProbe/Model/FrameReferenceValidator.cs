namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal static class FrameReferenceValidator {
    public static void EnsureStrictlyEarlier(
        AbsoluteFrameAddress containing,
        AbsoluteFrameAddress target) {
        if (target.FileNumber.Value > containing.FileNumber.Value ||
            (target.FileNumber == containing.FileNumber &&
                target.FrameTicket.OffsetBytes >= containing.FrameTicket.OffsetBytes)) {
            throw new InvalidDataException(
                $"Frame {containing} points to non-earlier Frame {target}.");
        }
    }
}
