namespace Atelia.TwoLegRotationProbe.Benchmarking;

public sealed record BenchmarkComponentIdentityV1 {
    public BenchmarkComponentIdentityV1(string id, int version) {
        BenchmarkV1Text.ValidateId(id, nameof(id));
        if (version <= 0) {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Id = id;
        Version = version;
    }

    public string Id { get; }

    public int Version { get; }
}

internal static class BenchmarkV1Identities {
    public static readonly BenchmarkComponentIdentityV1 ManifestSchema =
        new("two-leg-benchmark-manifest", 2);

    public static readonly BenchmarkComponentIdentityV1 ReportSchema =
        new("two-leg-benchmark-report", 1);

    public static readonly BenchmarkComponentIdentityV1 SourceFixture =
        new("trace-step0-single-a-full-base-then-b-anchor", 1);
}

internal static class BenchmarkV1Text {
    public static void ValidateId(string value, string parameterName) {
        if (string.IsNullOrEmpty(value) || !IsLowerAlphaNumeric(value[0])) {
            throw new ArgumentException(
                "An identifier must start with a lowercase ASCII letter or digit.",
                parameterName);
        }

        foreach (char character in value.AsSpan(1)) {
            if (!IsLowerAlphaNumeric(character) &&
                character is not ('.' or '_' or '/' or '-')) {
                throw new ArgumentException(
                    $"Identifier '{value}' contains an unsupported character.",
                    parameterName);
            }
        }
    }

    public static void ValidateSha256(string value, string parameterName) {
        if (value is null || value.Length != 64 ||
            value.Any(static character =>
                character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f'))) {
            throw new ArgumentException(
                "A SHA-256 value must contain exactly 64 lowercase hexadecimal digits.",
                parameterName);
        }
    }

    private static bool IsLowerAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
