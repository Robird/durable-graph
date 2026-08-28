namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>Size-only model of canonical unsigned Base128. This is not a wire writer.</summary>
internal static class CanonicalUnsignedBase128 {
    public static int GetEncodedWidth(ulong value) {
        int width = 1;
        while (value >= 0x80) {
            value >>= 7;
            width++;
        }

        return width;
    }
}
