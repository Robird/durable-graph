using System.Collections.ObjectModel;

namespace Atelia.TwoLegRotationProbe.Arena;

public enum StrategyTargetV1 {
    StayB,
    RotateC,
}

public enum StrategyUpdateWriteModeV1 {
    Base,
    Delta,
}

public readonly record struct StrategyUpdateWriteDecisionV1 {
    public StrategyUpdateWriteDecisionV1(
        uint objectId,
        StrategyUpdateWriteModeV1 mode) {
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ObjectId = objectId;
        Mode = mode;
    }

    public uint ObjectId { get; }

    public StrategyUpdateWriteModeV1 Mode { get; }
}

/// <summary>
/// Complete optional action choices for a Stay-B candidate.
/// </summary>
public sealed class StrategyStayDecisionV1 {
    private readonly ReadOnlyCollection<StrategyUpdateWriteDecisionV1>
        _updateDecisions;
    private readonly ReadOnlyCollection<uint> _unchangedMigrationObjectIds;

    public StrategyStayDecisionV1(
        IEnumerable<StrategyUpdateWriteDecisionV1> updateDecisions,
        IEnumerable<uint> unchangedMigrationObjectIds) {
        _updateDecisions = FreezeUpdateDecisions(
            updateDecisions,
            nameof(updateDecisions));
        _unchangedMigrationObjectIds = FreezeObjectIds(
            unchangedMigrationObjectIds,
            nameof(unchangedMigrationObjectIds));
    }

    public IReadOnlyList<StrategyUpdateWriteDecisionV1> UpdateDecisions =>
        _updateDecisions;

    public IReadOnlyList<uint> UnchangedMigrationObjectIds =>
        _unchangedMigrationObjectIds;

    internal static ReadOnlyCollection<StrategyUpdateWriteDecisionV1>
        FreezeUpdateDecisions(
            IEnumerable<StrategyUpdateWriteDecisionV1> decisions,
            string parameterName) {
        ArgumentNullException.ThrowIfNull(decisions, parameterName);
        StrategyUpdateWriteDecisionV1[] canonical = decisions
            .OrderBy(static decision => decision.ObjectId)
            .ToArray();
        for (int index = 1; index < canonical.Length; index++) {
            if (canonical[index - 1].ObjectId == canonical[index].ObjectId) {
                throw new ArgumentException(
                    $"Update decisions contain duplicate ObjectId " +
                    $"{canonical[index].ObjectId}.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(canonical);
    }

    internal static ReadOnlyCollection<uint> FreezeObjectIds(
        IEnumerable<uint> objectIds,
        string parameterName) {
        ArgumentNullException.ThrowIfNull(objectIds, parameterName);
        uint[] canonical = objectIds.Order().ToArray();
        for (int index = 1; index < canonical.Length; index++) {
            if (canonical[index - 1] == canonical[index]) {
                throw new ArgumentException(
                    $"ObjectId membership contains duplicate ObjectId {canonical[index]}.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(canonical);
    }
}

/// <summary>
/// Complete choices that remain optional for a Rotate-C candidate.
/// </summary>
public sealed class StrategyRotateDecisionV1 {
    private readonly ReadOnlyCollection<StrategyUpdateWriteDecisionV1>
        _bContainedUpdateDecisions;
    private readonly ReadOnlyCollection<uint> _bContainedNoChangeBaseObjectIds;

    public StrategyRotateDecisionV1(
        IEnumerable<StrategyUpdateWriteDecisionV1> bContainedUpdateDecisions,
        IEnumerable<uint> bContainedNoChangeBaseObjectIds) {
        _bContainedUpdateDecisions = StrategyStayDecisionV1.FreezeUpdateDecisions(
            bContainedUpdateDecisions,
            nameof(bContainedUpdateDecisions));
        _bContainedNoChangeBaseObjectIds = StrategyStayDecisionV1.FreezeObjectIds(
            bContainedNoChangeBaseObjectIds,
            nameof(bContainedNoChangeBaseObjectIds));
    }

    public IReadOnlyList<StrategyUpdateWriteDecisionV1>
        BContainedUpdateDecisions => _bContainedUpdateDecisions;

    public IReadOnlyList<uint> BContainedNoChangeBaseObjectIds =>
        _bContainedNoChangeBaseObjectIds;
}

/// <summary>
/// A strategy's complete, deterministic action selection for one Save step.
/// Arena planning, admission, execution, and measurement remain outside this value.
/// </summary>
public sealed class StrategySelectionV1 {
    public StrategySelectionV1(
        StrategyTargetV1 target,
        StrategyStayDecisionV1 stay,
        StrategyRotateDecisionV1 rotate) {
        if (!Enum.IsDefined(target)) {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        Target = target;
        Stay = stay ?? throw new ArgumentNullException(nameof(stay));
        Rotate = rotate ?? throw new ArgumentNullException(nameof(rotate));
    }

    public StrategyTargetV1 Target { get; }

    public StrategyStayDecisionV1 Stay { get; }

    public StrategyRotateDecisionV1 Rotate { get; }
}
