namespace Atelia.DurableGraph.Serialization;

/// <summary>
/// Encodes nullable bare payloads with zero for null and raw-header-plus-one for present values.
/// </summary>
internal static class NullablePayloadHeader {
    internal static uint EncodeNull() => 0;

    internal static uint EncodePresent(uint rawHeader) => checked(rawHeader + 1u);

    internal static bool TryDecode(uint encodedHeader, out uint rawHeader) {
        if (encodedHeader == 0) {
            rawHeader = 0;
            return false;
        }

        rawHeader = encodedHeader - 1u;
        return true;
    }
}
