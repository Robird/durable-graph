namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal enum RandomDomain : ulong {
    Lifecycle = 0x4C4946454359434C,
    ObjectCreate = 0x4F424A4352454154,
    ObjectUpdate = 0x4F424A5550444154,
}

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

    public RandomStream Fork(
        RandomDomain domain,
        int stepIndex,
        uint objectId = 0,
        int lane = 0) {
        if (!Enum.IsDefined(domain)) {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(stepIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(lane);

        ulong state = SplitMix64.Mix(_seed ^ RootSalt);
        state = Derive(state, (ulong)domain, DomainSalt);
        state = Derive(state, (uint)stepIndex, StepSalt);
        state = Derive(state, objectId, ObjectSalt);
        state = Derive(state, (uint)lane, LaneSalt);
        return new RandomStream(state);
    }

    private static ulong Derive(ulong state, ulong component, ulong salt) =>
        SplitMix64.Mix(state ^ SplitMix64.Mix(unchecked(component + salt)));
}

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
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        return NextInt(0, exclusiveMax);
    }

    public int NextInt(int inclusiveMin, int exclusiveMax) {
        if (inclusiveMin >= exclusiveMax) {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        ulong range = (ulong)((long)exclusiveMax - inclusiveMin);
        ulong threshold = unchecked(0UL - range) % range;
        ulong sample;
        do {
            sample = NextUInt64();
        }
        while (sample < threshold);

        return checked((int)(inclusiveMin + (long)(sample % range)));
    }
}

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
