using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>
/// Experimental relative-ticket token grammar: 0=None, 1=invalid, and values at least 2
/// contain a projected SizedPtr plus a current/previous-file selector in the low bit.
/// </summary>
internal static class ProvisionalRelativeFrameTicketCodec {
    public const ulong NoneToken = 0;
    public const ulong InvalidToken = 1;

    public static ulong EncodeOptional(RelativeFrameTicket? ticket) =>
        ticket is RelativeFrameTicket required
            ? EncodeRequired(required)
            : NoneToken;

    public static ulong EncodeRequired(RelativeFrameTicket ticket) {
        if (!RbfV040Layout.IsDurableGraphRelativeStartRepresentable(ticket.FrameTicket)) {
            throw new ArgumentOutOfRangeException(
                nameof(ticket),
                ticket,
                "The relative frame ticket exceeds the DurableGraph 512 GiB start-offset gate.");
        }

        ulong serialized = ProvisionalSizedPtrProjection.Serialize(ticket.FrameTicket);
        if ((serialized & (1UL << 63)) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(ticket),
                ticket,
                "The projected SizedPtr consumes the selector bit.");
        }

        ulong token = checked(serialized << 1) | (ticket.IsPreviousFile ? 1UL : 0UL);
        if (token < 2) {
            throw new ArgumentOutOfRangeException(
                nameof(ticket),
                ticket,
                "A required relative frame ticket cannot use a reserved token.");
        }

        return token;
    }

    public static RelativeFrameTicket DecodeRequired(ulong token) {
        if (token < 2) {
            throw new InvalidDataException(
                $"Relative frame ticket token {token} is not a required ticket.");
        }

        FrameTicket ticket;
        try {
            ticket = ProvisionalSizedPtrProjection.Deserialize(token >> 1);
        } catch (ArgumentOutOfRangeException exception) {
            throw new InvalidDataException(
                $"Relative frame ticket token {token} does not contain a valid frame ticket.",
                exception);
        }

        if (!RbfV040Layout.IsDurableGraphRelativeStartRepresentable(ticket)) {
            throw new InvalidDataException(
                $"Relative frame ticket token {token} exceeds the DurableGraph range.");
        }

        return new RelativeFrameTicket((token & 1) != 0, ticket);
    }

    public static RelativeFrameTicket? DecodeOptional(ulong token) => token switch {
        NoneToken => null,
        InvalidToken => throw new InvalidDataException(
            "Relative frame ticket token 1 is reserved and invalid."),
        _ => DecodeRequired(token),
    };
}
