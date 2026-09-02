using System.Collections.ObjectModel;

namespace Atelia.MultiSegmentStateStoreProbe.Workloads.Generation;

internal static class FixedCountLifecycle {
    public static LifecyclePlan Plan(
        ScenarioDefinition definition,
        IReadOnlyCollection<uint> preStepLiveObjectIds,
        RandomStream random) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(preStepLiveObjectIds);
        ArgumentNullException.ThrowIfNull(random);

        uint[] shuffled = preStepLiveObjectIds.Order().ToArray();
        for (int remaining = shuffled.Length; remaining > 1; remaining--) {
            int selected = random.NextInt(remaining);
            (shuffled[remaining - 1], shuffled[selected]) =
                (shuffled[selected], shuffled[remaining - 1]);
        }

        int removeCount = Math.Min(definition.MaxRemovePerLaterStep, shuffled.Length);
        int updateCount = Math.Min(
            definition.MaxUpdatePerLaterStep,
            shuffled.Length - removeCount);
        return new LifecyclePlan(
            definition.CreatePerLaterStep,
            shuffled[..removeCount],
            shuffled[removeCount..(removeCount + updateCount)]);
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
        CreateCount = createCount;
        _removeObjectIds = Array.AsReadOnly(removeObjectIds.ToArray());
        _updateObjectIds = Array.AsReadOnly(updateObjectIds.ToArray());
    }

    public int CreateCount { get; }
    public IReadOnlyList<uint> RemoveObjectIds => _removeObjectIds;
    public IReadOnlyList<uint> UpdateObjectIds => _updateObjectIds;
}
