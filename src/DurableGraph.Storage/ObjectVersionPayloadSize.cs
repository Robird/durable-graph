namespace Atelia.DurableGraph.Storage;

/// <summary>Measures the provisional v3 object payload, excluding its ObjectId and shared Frame/map bytes.</summary>
public static class ObjectVersionPayloadSize {
    /// <summary>Returns the exact kind, body-length prefix, and Base body byte count.</summary>
    /// <remarks>This measures a payload; it does not check whether a complete Frame will fit.</remarks>
    public static long GetBasePayloadBytes(int bodyLength) {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyLength);
        return checked(1L + GetVarUIntBytes((uint)bodyLength) + bodyLength);
    }

    /// <summary>Returns the exact Delta payload byte count in its containing file scope.</summary>
    /// <remarks>
    /// Includes kind, prior address, body-length prefix and body, excluding ObjectId and shared Frame/map bytes.
    /// This does not check whether a complete Frame will fit or whether the prior precedes the containing Frame.
    /// </remarks>
    public static long GetDeltaPayloadBytes(int bodyLength, FrameAddress prior, FileScope scope) {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyLength);
        FrameAddressValidator.ValidateRequired(prior, nameof(prior));
        scope.ValidateRequired(nameof(scope));
        uint distance = scope.ToBackwardFileDistance(prior.FileNumber);
        return checked(1L + GetVarUIntBytes(distance) + GetVarUIntBytes(prior.FrameTicket.Serialize())
            + GetVarUIntBytes((uint)bodyLength) + bodyLength);
    }

    /// <summary>Returns a scope-independent upper bound for a Delta payload.</summary>
    /// <remarks>
    /// Includes kind, prior address, body-length prefix and body. Only the prior's
    /// UInt32 backward file distance is estimated, at its maximum five bytes;
    /// for any legal target scope the excess over actual payload size is zero to four bytes.
    /// This does not check whether a complete Frame will fit.
    /// </remarks>
    public static long EstimateDeltaPayloadBytesUpperBound(int bodyLength, FrameAddress prior) {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyLength);
        FrameAddressValidator.ValidateRequired(prior, nameof(prior));
        return checked(1L + 5 + GetVarUIntBytes(prior.FrameTicket.Serialize())
            + GetVarUIntBytes((uint)bodyLength) + bodyLength);
    }

    private static int GetVarUIntBytes(ulong value) {
        int length = 1;
        while (value >= 0x80) {
            value >>= 7;
            length++;
        }
        return length;
    }
}
