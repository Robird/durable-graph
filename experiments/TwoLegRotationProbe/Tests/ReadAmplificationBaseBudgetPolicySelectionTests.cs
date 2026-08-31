using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ReadAmplificationBaseBudgetPolicySelectionTests {
    private static readonly FrameTicket Ticket = new(
        RbfV040Layout.HeaderFenceBytes,
        RbfV040Layout.MinFrameLengthBytes);

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    [InlineData(51, false)]
    public void Target_uses_strict_unrounded_evacuation_share(
        int evacuationBytes,
        bool expectedRotate) {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(1, evacuationBytes, isADependent: true,
                historyBytes: evacuationBytes),
            Insert(2, 1000 - evacuationBytes));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(1000, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(evacuationBytes,
            projection.ADependentEvacuationBasePayloadBytes);
        Assert.Equal(50, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal(
            expectedRotate ? StrategyTargetV1.RotateC : StrategyTargetV1.StayB,
            selection.Target);
    }

    [Fact]
    public void Target_compares_against_unrounded_share_not_floored_budget() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(1, baseBytes: 5, isADependent: true, historyBytes: 5),
            Insert(2, baseBytes: 96));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(101, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(5, projection.ADependentEvacuationBasePayloadBytes);
        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
    }

    [Fact]
    public void Empty_post_live_graph_rotates_with_zero_budget() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project();

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
        Assert.Equal(0, selection.PreferredBasePayloadBudgetBytes);
        Assert.Empty(selection.StayB.UpdateDecisions);
        Assert.Empty(selection.RotateC.BContainedUpdateDecisions);
    }

    [Fact]
    public void Amplification_threshold_is_strict_at_decimal_equality() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(1, baseBytes: 100, historyBytes: 298, deltaBytes: 1),
            Update(2, baseBytes: 100, historyBytes: 299, deltaBytes: 1),
            Update(3, baseBytes: 100, historyBytes: 300, deltaBytes: 1));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 1m);

        AssertModes(selection.StayB.UpdateDecisions,
            (1, StrategyUpdateWriteModeV1.Delta),
            (2, StrategyUpdateWriteModeV1.Delta),
            (3, StrategyUpdateWriteModeV1.Base));
        AssertModes(selection.RotateC.BContainedUpdateDecisions,
            (1, StrategyUpdateWriteModeV1.Delta),
            (2, StrategyUpdateWriteModeV1.Delta),
            (3, StrategyUpdateWriteModeV1.Base));
    }

    [Fact]
    public void NoChange_amplification_threshold_is_strict_at_decimal_equality() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(1, baseBytes: 100, isADependent: true, historyBytes: 300),
            NoChange(2, baseBytes: 100, isADependent: true, historyBytes: 301),
            NoChange(3, baseBytes: 100, isADependent: false, historyBytes: 300),
            NoChange(4, baseBytes: 100, isADependent: false, historyBytes: 301));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 1m);

        Assert.Equal([2U], selection.StayB.UnchangedMigrationObjectIds);
        Assert.Equal([4U], selection.RotateC.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Base_no_larger_than_Delta_is_unbudgeted_dominant() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(1, baseBytes: 10, historyBytes: 0, deltaBytes: 10));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 100m,
            budgetFraction: 0.01m);

        Assert.Equal(0, selection.PreferredBasePayloadBudgetBytes);
        AssertMode(selection.StayB.UpdateDecisions, 1,
            StrategyUpdateWriteModeV1.Base);
        AssertMode(selection.RotateC.BContainedUpdateDecisions, 1,
            StrategyUpdateWriteModeV1.Base);
    }

    [Fact]
    public void Finite_ratio_does_not_overflow_at_maximum_decimal_limit() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(1, baseBytes: int.MaxValue, historyBytes: long.MaxValue - 1,
                deltaBytes: 1));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: decimal.MaxValue,
            budgetFraction: 1m);

        AssertMode(selection.StayB.UpdateDecisions, 1,
            StrategyUpdateWriteModeV1.Delta);
        AssertMode(selection.RotateC.BContainedUpdateDecisions, 1,
            StrategyUpdateWriteModeV1.Delta);
    }

    [Fact]
    public void Equal_ratios_use_ObjectId_as_budget_tie_break() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(20, baseBytes: 6, historyBytes: 11, deltaBytes: 1),
            Update(10, baseBytes: 6, historyBytes: 11, deltaBytes: 1));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 1.5m,
            budgetFraction: 0.5m);

        Assert.Equal(6, selection.PreferredBasePayloadBudgetBytes);
        AssertMode(selection.StayB.UpdateDecisions, 10,
            StrategyUpdateWriteModeV1.Base);
        AssertMode(selection.StayB.UpdateDecisions, 20,
            StrategyUpdateWriteModeV1.Delta);
        AssertMode(selection.RotateC.BContainedUpdateDecisions, 10,
            StrategyUpdateWriteModeV1.Base);
        AssertMode(selection.RotateC.BContainedUpdateDecisions, 20,
            StrategyUpdateWriteModeV1.Delta);
    }

    [Fact]
    public void Preferred_Base_budget_is_floored_in_payload_bytes() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Insert(1, 101));
        ReadAmplificationBaseBudgetPolicyParameters parameters = new(3m, 0.05m);

        ReadAmplificationBaseBudgetPolicySelection selection =
            ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);

        Assert.Same(projection, selection.Projection);
        Assert.Same(parameters, selection.Parameters);
        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
    }

    [Fact]
    public void Unmotivated_A_NoChange_is_not_selected() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(10, baseBytes: 600, isADependent: true,
                historyBytes: 600),
            Insert(20, baseBytes: 400));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(StrategyTargetV1.StayB, selection.Target);
        Assert.Equal(50, selection.PreferredBasePayloadBudgetBytes);
        Assert.Empty(selection.StayB.UnchangedMigrationObjectIds);
    }

    [Fact]
    public void Motivated_oversized_first_candidate_is_admitted_alone() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(10, baseBytes: 600, isADependent: true,
                historyBytes: 2400),
            Update(20, baseBytes: 10, historyBytes: 35, deltaBytes: 5),
            Insert(30, baseBytes: 390));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(50, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal([10U], selection.StayB.UnchangedMigrationObjectIds);
        AssertMode(selection.StayB.UpdateDecisions, 20,
            StrategyUpdateWriteModeV1.Delta);
    }

    [Fact]
    public void Motivated_selection_takes_longest_fitting_sorted_prefix() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(10, baseBytes: 6, historyBytes: 59, deltaBytes: 1),
            NoChange(20, baseBytes: 5, isADependent: true, historyBytes: 25),
            Update(30, baseBytes: 4, historyBytes: 15, deltaBytes: 1),
            NoChange(35, baseBytes: 1, isADependent: true, historyBytes: 3),
            Insert(40, baseBytes: 8));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 2m,
            budgetFraction: 0.5m);

        Assert.Equal(12, selection.PreferredBasePayloadBudgetBytes);
        AssertModes(selection.StayB.UpdateDecisions,
            (10, StrategyUpdateWriteModeV1.Base),
            (30, StrategyUpdateWriteModeV1.Delta));
        Assert.Equal([20U], selection.StayB.UnchangedMigrationObjectIds);
    }

    [Fact]
    public void Stay_orders_motivated_NoChange_and_Update_together() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(10, baseBytes: 5, historyBytes: 49, deltaBytes: 1),
            NoChange(20, baseBytes: 6, isADependent: true,
                historyBytes: 18));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 2m,
            budgetFraction: 0.5m);

        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        AssertMode(selection.StayB.UpdateDecisions, 10,
            StrategyUpdateWriteModeV1.Base);
        Assert.Empty(selection.StayB.UnchangedMigrationObjectIds);
    }

    [Fact]
    public void Zero_denominator_orders_infinity_above_zero_over_zero() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(1, baseBytes: 0, isADependent: true, historyBytes: 0),
            NoChange(2, baseBytes: 0, isADependent: true, historyBytes: 1),
            Insert(3, 1));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 1m,
            budgetFraction: 0.5m);

        Assert.Equal([2U], selection.StayB.UnchangedMigrationObjectIds);
    }

    [Fact]
    public void Rotate_omits_mandatory_A_and_stops_at_oversized_optional_prefix() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Insert(1, 9),
            Remove(2, baseBytes: 4, isADependent: true),
            Update(3, baseBytes: 4, historyBytes: 3, deltaBytes: 1,
                isADependent: true),
            NoChange(4, baseBytes: 4, isADependent: true, historyBytes: 4),
            Update(5, baseBytes: 2, historyBytes: 0, deltaBytes: 2),
            Update(6, baseBytes: 3, historyBytes: 29, deltaBytes: 1),
            Update(7, baseBytes: 2, historyBytes: 7, deltaBytes: 1),
            NoChange(8, baseBytes: 1, isADependent: false, historyBytes: 1));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 2m,
            budgetFraction: 0.4m);

        Assert.Equal(25, projection.PostLiveGraphBasePayloadBytes);
        Assert.Equal(8, projection.ADependentEvacuationBasePayloadBytes);
        Assert.Equal(10, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
        AssertModes(selection.RotateC.BContainedUpdateDecisions,
            (5, StrategyUpdateWriteModeV1.Base),
            (6, StrategyUpdateWriteModeV1.Delta),
            (7, StrategyUpdateWriteModeV1.Delta));
        Assert.DoesNotContain(selection.RotateC.BContainedUpdateDecisions,
            static decision => decision.ObjectId == 3);
        Assert.Empty(selection.RotateC.BContainedNoChangeBaseObjectIds);
        Assert.Equal([3U, 5U, 6U, 7U],
            selection.StayB.UpdateDecisions.Select(static decision =>
                decision.ObjectId));
    }

    [Fact]
    public void Rotate_can_select_motivated_B_contained_NoChange() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(10, baseBytes: 1, isADependent: true, historyBytes: 1),
            NoChange(20, baseBytes: 4, isADependent: false, historyBytes: 20),
            Insert(30, baseBytes: 15));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.5m);

        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
        Assert.Equal([20U],
            selection.RotateC.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Rotate_with_no_evacuation_admits_oversized_first_motive_alone() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(10, baseBytes: 10, isADependent: false, historyBytes: 40),
            Insert(20, baseBytes: 90));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal([10U],
            selection.RotateC.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Rotate_does_not_overshoot_optional_budget_after_mandatory_evacuation() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            NoChange(10, baseBytes: 1, isADependent: true, historyBytes: 1),
            NoChange(20, baseBytes: 10, isADependent: false, historyBytes: 40),
            Insert(30, baseBytes: 89));

        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            projection,
            readLimit: 3m,
            budgetFraction: 0.05m);

        Assert.Equal(StrategyTargetV1.RotateC, selection.Target);
        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        Assert.Empty(selection.RotateC.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Repeated_selection_is_deterministic_and_preserves_inputs() {
        ReadAmplificationBaseBudgetPolicyProjection projection = Project(
            Update(30, baseBytes: 4, historyBytes: 11, deltaBytes: 1),
            NoChange(20, baseBytes: 5, isADependent: true, historyBytes: 20),
            Insert(10, 3));
        ReadAmplificationBaseBudgetPolicyParameters parameters = new(2m, 0.5m);

        ReadAmplificationBaseBudgetPolicySelection first =
            ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);
        ReadAmplificationBaseBudgetPolicySelection second =
            ReadAmplificationBaseBudgetPolicy.Select(projection, parameters);

        Assert.Same(projection, first.Projection);
        Assert.Same(parameters, first.Parameters);
        Assert.Equal(first.Target, second.Target);
        Assert.Equal(first.PreferredBasePayloadBudgetBytes,
            second.PreferredBasePayloadBudgetBytes);
        Assert.Equal(first.StayB.UpdateDecisions, second.StayB.UpdateDecisions);
        Assert.Equal(first.StayB.UnchangedMigrationObjectIds,
            second.StayB.UnchangedMigrationObjectIds);
        Assert.Equal(first.RotateC.BContainedUpdateDecisions,
            second.RotateC.BContainedUpdateDecisions);
        Assert.Equal(first.RotateC.BContainedNoChangeBaseObjectIds,
            second.RotateC.BContainedNoChangeBaseObjectIds);
    }

    private static ReadAmplificationBaseBudgetPolicySelection Select(
        ReadAmplificationBaseBudgetPolicyProjection projection,
        decimal readLimit,
        decimal budgetFraction) => ReadAmplificationBaseBudgetPolicy.Select(
            projection,
            new ReadAmplificationBaseBudgetPolicyParameters(
                readLimit,
                budgetFraction));

    private static ReadAmplificationBaseBudgetPolicyProjection Project(
        params FactSpec[] specifications) {
        AbsoluteFrameAddress published = new(fileNumber: 2, Ticket);
        List<SourceObjectFact> parent = [];
        List<NormalizedSaveFact> facts = [];
        foreach (FactSpec specification in specifications) {
            if (specification.Kind == FactSpecKind.Insert) {
                facts.Add(new NormalizedInsertFact(
                    specification.ObjectId,
                    new LogicalObjectState(
                        specification.BaseBytes,
                        LogicalVersionOrdinal: 1)));
                continue;
            }

            SourceObjectFact source = CreateSource(specification, published);
            parent.Add(source);
            facts.Add(specification.Kind switch {
                FactSpecKind.Update => new NormalizedUpdateFact(
                    source,
                    new LogicalObjectState(
                        specification.BaseBytes,
                        LogicalVersionOrdinal: 2),
                    specification.DeltaBytes!.Value),
                FactSpecKind.Remove => new NormalizedRemoveFact(source),
                FactSpecKind.NoChange => new NormalizedNoChangeFact(source),
                _ => throw new ArgumentOutOfRangeException(),
            });
        }

        NormalizedSaveFacts normalized = new(
            previousFileNumber: 1,
            currentFileNumber: 2,
            published,
            parent,
            facts);
        return ReadAmplificationBaseBudgetPolicyProjection.Create(
            StrategyStepViewV1.Create(normalized));
    }

    private static SourceObjectFact CreateSource(
        FactSpec specification,
        AbsoluteFrameAddress head) {
        AbsoluteFrameAddress baseAddress = specification.IsADependent
            ? new AbsoluteFrameAddress(fileNumber: 1, Ticket)
            : head;
        AbsoluteFrameAddress[] path = specification.IsADependent
            ? [head, baseAddress]
            : [head];
        return new SourceObjectFact(
            specification.ObjectId,
            new LogicalObjectState(
                specification.BaseBytes,
                LogicalVersionOrdinal: 1),
            head,
            baseAddress,
            specification.HistoryBytes,
            path);
    }

    private static void AssertMode(
        IReadOnlyList<StrategyUpdateWriteDecisionV1> decisions,
        uint objectId,
        StrategyUpdateWriteModeV1 expected) => Assert.Equal(
        expected,
        Assert.Single(decisions, decision => decision.ObjectId == objectId).Mode);

    private static void AssertModes(
        IReadOnlyList<StrategyUpdateWriteDecisionV1> decisions,
        params (uint ObjectId, StrategyUpdateWriteModeV1 Mode)[] expected) =>
        Assert.Equal(
        expected,
        decisions.Select(static decision =>
            (decision.ObjectId, decision.Mode)).ToArray());

    private static FactSpec Insert(uint objectId, int baseBytes) => new(
        objectId,
        FactSpecKind.Insert,
        baseBytes,
        IsADependent: false,
        HistoryBytes: 0,
        DeltaBytes: null);

    private static FactSpec Update(
        uint objectId,
        int baseBytes,
        long historyBytes,
        int deltaBytes,
        bool isADependent = false) => new(
        objectId,
        FactSpecKind.Update,
        baseBytes,
        isADependent,
        historyBytes,
        deltaBytes);

    private static FactSpec Remove(
        uint objectId,
        int baseBytes,
        bool isADependent) => new(
        objectId,
        FactSpecKind.Remove,
        baseBytes,
        isADependent,
        HistoryBytes: baseBytes,
        DeltaBytes: null);

    private static FactSpec NoChange(
        uint objectId,
        int baseBytes,
        bool isADependent,
        long historyBytes) => new(
        objectId,
        FactSpecKind.NoChange,
        baseBytes,
        isADependent,
        historyBytes,
        DeltaBytes: null);

    private enum FactSpecKind {
        Insert,
        Update,
        Remove,
        NoChange,
    }

    private sealed record FactSpec(
        uint ObjectId,
        FactSpecKind Kind,
        int BaseBytes,
        bool IsADependent,
        long HistoryBytes,
        int? DeltaBytes);
}
