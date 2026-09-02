namespace Atelia.MultiSegmentStateStoreProbe.Model;

internal readonly record struct FileScope {
    public FileScope(FileNumber originFileNumber) {
        if (originFileNumber.Value == 0) {
            throw new ArgumentOutOfRangeException(nameof(originFileNumber));
        }

        OriginFileNumber = originFileNumber;
    }

    public FileNumber OriginFileNumber { get; }

    public RelativeFrameTicket Relativize(AbsoluteFrameAddress target) {
        if (target.FileNumber.Value > OriginFileNumber.Value) {
            throw new InvalidDataException(
                $"Future file {target.FileNumber} cannot be referenced from file {OriginFileNumber}.");
        }

        return new RelativeFrameTicket(
            checked(OriginFileNumber.Value - target.FileNumber.Value),
            target.FrameTicket);
    }

    public AbsoluteFrameAddress Resolve(RelativeFrameTicket reference) {
        reference.ValidateRequired();
        if (reference.BackwardFileDistance >= OriginFileNumber.Value) {
            throw new InvalidDataException(
                $"Backward file distance {reference.BackwardFileDistance} underflows " +
                $"1-based origin file {OriginFileNumber}.");
        }

        return new AbsoluteFrameAddress(
            new FileNumber(checked(
                OriginFileNumber.Value - reference.BackwardFileDistance)),
            reference.FrameTicket);
    }

    public AbsoluteFrameAddress ResolveEarlier(
        FrameTicket containingFrameTicket,
        RelativeFrameTicket reference) {
        AbsoluteFrameAddress containing = new(
            OriginFileNumber,
            containingFrameTicket);
        AbsoluteFrameAddress target = Resolve(reference);
        FrameReferenceValidator.EnsureStrictlyEarlier(containing, target);
        return target;
    }
}
