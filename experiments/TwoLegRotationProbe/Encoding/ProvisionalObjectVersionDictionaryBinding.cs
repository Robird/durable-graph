using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>Field-contextual OVD binding tokens. Self is deliberately not a general address.</summary>
internal static class ProvisionalObjectVersionDictionaryBinding {
    public const ulong InvalidToken = 0;
    public const ulong SelfToken = 1;

    public static ulong EncodeSelf() => SelfToken;

    public static ulong EncodeExternal(RelativeFrameTicket ticket) =>
        ProvisionalRelativeFrameTicketCodec.EncodeRequired(ticket);

    public static AbsoluteFrameAddress Resolve(
        ulong token,
        uint originFileNumber,
        FrameTicket containingFrameTicket) {
        ArgumentOutOfRangeException.ThrowIfZero(originFileNumber);
        AbsoluteFrameAddress containingAddress = new(originFileNumber, containingFrameTicket);
        if (token == InvalidToken) {
            throw new InvalidDataException("OVD binding token 0 is invalid.");
        }

        if (token == SelfToken) {
            return containingAddress;
        }

        RelativeFrameTicket relative =
            ProvisionalRelativeFrameTicketCodec.DecodeRequired(token);
        AbsoluteFrameAddress resolved;
        try {
            resolved = new FileScope(originFileNumber).Resolve(relative);
        } catch (InvalidOperationException exception) {
            throw new InvalidDataException(
                $"OVD binding token {token} cannot be resolved from file {originFileNumber}.",
                exception);
        }

        if (resolved == containingAddress) {
            throw new InvalidDataException(
                "An external OVD binding token aliases its containing frame; use Self instead.");
        }

        return resolved;
    }
}
