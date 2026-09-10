namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>Compares complete persistent scalar representations where numeric equality would lose state.</summary>
public static class ScalarStateEquality {
    /// <summary>Compares decimal's four public representation words, including scale and the sign of zero.</summary>
    public static bool DecimalEquals(in decimal left, in decimal right) {
        Span<int> leftBits = stackalloc int[4];
        Span<int> rightBits = stackalloc int[4];
        decimal.GetBits(left, leftBits);
        decimal.GetBits(right, rightBits);
        return leftBits.SequenceEqual(rightBits);
    }
}
