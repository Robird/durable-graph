namespace Atelia.DurableGraph.Storage;

internal static class FrameAddressValidator {
    internal static void ValidateRequired(
        FrameAddress address,
        string parameterName) {
        if (address.FileNumber == 0 || address.FrameTicket.Length == 0) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                address,
                "A FrameAddress requires a 1-based FileNumber and non-empty ticket.");
        }
    }

    internal static void EnsureStrictlyEarlier(
        FrameAddress containing,
        FrameAddress target) {
        ValidateRequired(containing, nameof(containing));
        ValidateRequired(target, nameof(target));
        if (target.FileNumber > containing.FileNumber ||
            (target.FileNumber == containing.FileNumber &&
                target.FrameTicket.Offset >= containing.FrameTicket.Offset)) {
            throw new InvalidDataException(
                $"Frame {containing} cannot reference non-earlier Frame {target}.");
        }
    }
}
