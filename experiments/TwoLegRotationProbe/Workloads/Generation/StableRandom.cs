namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

/// <summary>
/// Derives deterministic, independently consumable random streams from stable keys.
/// </summary>
internal sealed class StableRandom {
    private const ulong RootSalt = 0xD1B54A32D192ED03;
    private const ulong DomainSalt = 0x8CB92BA72F3D8DD7;
    private const ulong StepSalt = 0xDB4F0B9175AE2165;
    private const ulong ObjectSalt = 0xBBE0563303A4615F;
    private const ulong LaneSalt = 0xA0F2EC75A1FE1575;

    private readonly ulong _seed;

    public StableRandom(ulong seed) {
        _seed = seed;
    }

    /// <summary>
    /// Returns a local stream whose state depends only on this root seed and the supplied key.
    /// Consuming the returned stream does not affect this root or any other fork.
    /// </summary>
    public RandomStream Fork(
        RandomDomain domain,
        int stepIndex,
        uint objectId = 0,
        int lane = 0) {
        ValidateDomain(domain);
        ArgumentOutOfRangeException.ThrowIfNegative(stepIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(lane);

        ulong state = SplitMix64.Mix(_seed ^ RootSalt);
        state = Derive(state, (ulong)domain, DomainSalt);
        state = Derive(state, (uint)stepIndex, StepSalt);
        state = Derive(state, objectId, ObjectSalt);
        state = Derive(state, (uint)lane, LaneSalt);
        return new RandomStream(state);
    }

    private static ulong Derive(ulong state, ulong component, ulong salt) {
        return SplitMix64.Mix(state ^ SplitMix64.Mix(unchecked(component + salt)));
    }

    private static void ValidateDomain(RandomDomain domain) {
        if (domain is not RandomDomain.Lifecycle
            and not RandomDomain.ObjectCreate
            and not RandomDomain.ObjectUpdate) {
            throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown random domain.");
        }
    }
}

/// <summary>
/// A mutable local SplitMix64 stream. Do not share a stream between independently replayable decisions.
/// </summary>
internal sealed class RandomStream {
    private ulong _state;

    internal RandomStream(ulong initialState) {
        _state = initialState;
    }

    public ulong NextUInt64() {
        _state = unchecked(_state + SplitMix64.Gamma);
        return SplitMix64.Mix(_state);
    }

    public int NextInt(int exclusiveMax) {
        if (exclusiveMax <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveMax),
                exclusiveMax,
                "The exclusive upper bound must be positive.");
        }

        return NextInt(0, exclusiveMax);
    }

    public int NextInt(int inclusiveMin, int exclusiveMax) {
        if (inclusiveMin >= exclusiveMax) {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveMax),
                exclusiveMax,
                "The exclusive upper bound must be greater than the inclusive lower bound.");
        }

        ulong range = (ulong)((long)exclusiveMax - inclusiveMin);
        ulong threshold = unchecked(0UL - range) % range;
        ulong sample;
        do {
            sample = NextUInt64();
        }
        while (sample < threshold);

        long result = inclusiveMin + (long)(sample % range);
        return (int)result;
    }
}

/// <summary>
/// Fixed SplitMix64 transition and finalizer from Steele, Lea, and Flood.
/// This local copy deliberately avoids <see cref="Random"/> and other runtime-versioned behavior.
/// </summary>
internal static class SplitMix64 {
    public const ulong Gamma = 0x9E3779B97F4A7C15;

    public static ulong Mix(ulong value) {
        unchecked {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
            return value ^ (value >> 31);
        }
    }
}
