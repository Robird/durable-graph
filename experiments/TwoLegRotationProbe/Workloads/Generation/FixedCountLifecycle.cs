using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

internal static class FixedCountLifecycle {
    public static LifecyclePlan Plan(
        ScenarioDefinition definition,
        IReadOnlyCollection<uint> preStepLiveObjectIds,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(preStepLiveObjectIds);
        ArgumentNullException.ThrowIfNull(random);

        uint[] shuffledIds = preStepLiveObjectIds.Order().ToArray();
        Shuffle(shuffledIds, random);

        int removeCount = Math.Min(
            definition.MaxRemovePerLaterStep,
            shuffledIds.Length);
        int updateCount = Math.Min(
            definition.MaxUpdatePerLaterStep,
            shuffledIds.Length - removeCount);

        uint[] removeIds = shuffledIds[..removeCount];
        uint[] updateIds = shuffledIds[removeCount..(removeCount + updateCount)];
        return new LifecyclePlan(
            definition.CreatePerLaterStep,
            removeIds,
            updateIds);
    }

    private static void Shuffle(uint[] values, RandomStream random) {
        for (int remaining = values.Length; remaining > 1; remaining--) {
            int selected = random.NextInt(remaining);
            (values[remaining - 1], values[selected]) = (values[selected], values[remaining - 1]);
        }
    }
}

internal sealed class LifecyclePlan {
    private readonly ReadOnlyCollection<uint> _removeObjectIds;
    private readonly ReadOnlyCollection<uint> _updateObjectIds;

    public LifecyclePlan(
        int createCount,
        IEnumerable<uint> removeObjectIds,
        IEnumerable<uint> updateObjectIds) {
        ArgumentOutOfRangeException.ThrowIfNegative(createCount);
        ArgumentNullException.ThrowIfNull(removeObjectIds);
        ArgumentNullException.ThrowIfNull(updateObjectIds);

        CreateCount = createCount;
        _removeObjectIds = Array.AsReadOnly(removeObjectIds.ToArray());
        _updateObjectIds = Array.AsReadOnly(updateObjectIds.ToArray());
    }

    public int CreateCount { get; }

    public IReadOnlyList<uint> RemoveObjectIds => _removeObjectIds;

    public IReadOnlyList<uint> UpdateObjectIds => _updateObjectIds;
}
