namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal static class BackwardFrameReferenceResolver {
    public static BackwardFrameReference Relativize(
        FileNumber originFileNumber,
        AbsoluteFrameAddress target) {
        if (target.FileNumber.Value > originFileNumber.Value) {
            throw new InvalidDataException(
                $"Future file {target.FileNumber} cannot be referenced from file {originFileNumber}.");
        }

        uint distance = checked(originFileNumber.Value - target.FileNumber.Value);
        return new BackwardFrameReference(distance, target.FrameTicketCode);
    }

    public static AbsoluteFrameAddress Resolve(
        FileNumber originFileNumber,
        BackwardFrameReference reference) {
        if (reference.BackwardFileDistance >= originFileNumber.Value) {
            throw new InvalidDataException(
                $"Backward file distance {reference.BackwardFileDistance} underflows " +
                $"1-based origin file {originFileNumber}.");
        }

        FileNumber targetFileNumber = new(
            checked(originFileNumber.Value - reference.BackwardFileDistance));
        return new AbsoluteFrameAddress(
            targetFileNumber,
            reference.FrameTicketCode);
    }
}
