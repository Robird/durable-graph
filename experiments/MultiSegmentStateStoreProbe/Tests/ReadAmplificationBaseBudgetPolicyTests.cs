using Atelia.MultiSegmentStateStoreProbe.Policies;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class ReadAmplificationBaseBudgetPolicyTests {
    [Fact]
    public void Update_and_NoChange_thresholds_are_strict_at_equality() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 3m,
            budgetFraction: 1m,
            Update(1, baseBytes: 100, historyBytes: 299, deltaBytes: 1),
            Update(2, baseBytes: 100, historyBytes: 300, deltaBytes: 1),
            NoChange(3, baseBytes: 100, historyBytes: 300),
            NoChange(4, baseBytes: 100, historyBytes: 301));

        AssertModes(
            selection,
            (1, ObjectVersionWriteMode.Delta),
            (2, ObjectVersionWriteMode.Base));
        Assert.Equal([4U], selection.SameStateRebaseObjectIds);
    }

    [Fact]
    public void Preferred_Base_budget_floors_the_unrounded_graph_fraction() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 3m,
            budgetFraction: 0.05m,
            Insert(1, 101),
            ReadAmplificationBaseBudgetPolicyFact.Remove(
                2,
                sourceReconstructionPayloadBytes: 999));

        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        Assert.Empty(selection.UpdateDecisions);
        Assert.Empty(selection.SameStateRebaseObjectIds);
    }

    [Fact]
    public void Base_no_larger_than_Delta_is_unbudgeted_dominant() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: decimal.MaxValue,
            budgetFraction: 0.01m,
            Update(1, baseBytes: 10, historyBytes: 0, deltaBytes: 10));

        Assert.Equal(0, selection.PreferredBasePayloadBudgetBytes);
        AssertModes(selection, (1, ObjectVersionWriteMode.Base));
    }

    [Fact]
    public void First_motivated_indivisible_object_can_cross_the_soft_budget_alone() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 3m,
            budgetFraction: 0.05m,
            NoChange(10, baseBytes: 60, historyBytes: 240),
            Update(20, baseBytes: 10, historyBytes: 35, deltaBytes: 5),
            Insert(30, 30));

        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal([10U], selection.SameStateRebaseObjectIds);
        AssertModes(selection, (20, ObjectVersionWriteMode.Delta));
    }

    [Fact]
    public void Motivated_selection_stops_at_the_first_nonfitting_sorted_candidate() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 2m,
            budgetFraction: 0.5m,
            Update(10, baseBytes: 6, historyBytes: 59, deltaBytes: 1),
            NoChange(20, baseBytes: 5, historyBytes: 25),
            Update(30, baseBytes: 4, historyBytes: 15, deltaBytes: 1),
            NoChange(35, baseBytes: 1, historyBytes: 3),
            Insert(40, 8));

        Assert.Equal(12, selection.PreferredBasePayloadBudgetBytes);
        AssertModes(
            selection,
            (10, ObjectVersionWriteMode.Base),
            (30, ObjectVersionWriteMode.Delta));
        Assert.Equal([20U], selection.SameStateRebaseObjectIds);
        Assert.DoesNotContain(35U, selection.SameStateRebaseObjectIds);
    }

    [Fact]
    public void Motivated_Update_and_NoChange_share_ratio_and_ObjectId_ordering() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 2m,
            budgetFraction: 0.5m,
            Update(20, baseBytes: 5, historyBytes: 14, deltaBytes: 1),
            NoChange(10, baseBytes: 5, historyBytes: 15));

        Assert.Equal(5, selection.PreferredBasePayloadBudgetBytes);
        AssertModes(selection, (20, ObjectVersionWriteMode.Delta));
        Assert.Equal([10U], selection.SameStateRebaseObjectIds);
    }

    [Fact]
    public void Zero_denominator_treats_zero_over_zero_as_one_and_positive_over_zero_as_infinity() {
        ReadAmplificationBaseBudgetPolicySelection selection = Select(
            readLimit: 1m,
            budgetFraction: 0.5m,
            NoChange(1, baseBytes: 0, historyBytes: 0),
            NoChange(2, baseBytes: 0, historyBytes: 1),
            Insert(3, 1));

        Assert.Equal(0, selection.PreferredBasePayloadBudgetBytes);
        Assert.Equal([2U], selection.SameStateRebaseObjectIds);
    }

    [Fact]
    public void Repeated_selection_is_deterministic_and_does_not_mutate_inputs() {
        ReadAmplificationBaseBudgetPolicyFact update =
            Update(30, baseBytes: 4, historyBytes: 11, deltaBytes: 1);
        ReadAmplificationBaseBudgetPolicyFact noChange =
            NoChange(20, baseBytes: 5, historyBytes: 20);
        ReadAmplificationBaseBudgetPolicyFact insert = Insert(10, 3);
        List<ReadAmplificationBaseBudgetPolicyFact> source = [update, noChange, insert];
        ReadAmplificationBaseBudgetPolicyInput input = new(source);
        ReadAmplificationBaseBudgetPolicyParameters parameters = new(2m, 0.5m);

        ReadAmplificationBaseBudgetPolicySelection first =
            ReadAmplificationBaseBudgetPolicy.Select(input, parameters);
        ReadAmplificationBaseBudgetPolicySelection second =
            ReadAmplificationBaseBudgetPolicy.Select(input, parameters);

        Assert.Equal(first.PreferredBasePayloadBudgetBytes,
            second.PreferredBasePayloadBudgetBytes);
        Assert.Equal(first.UpdateDecisions, second.UpdateDecisions);
        Assert.Equal(first.SameStateRebaseObjectIds, second.SameStateRebaseObjectIds);
        Assert.Equal([update, noChange, insert], source);
        Assert.Equal([insert, noChange, update], input.Facts);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 0)]
    [InlineData(1, 1.1)]
    public void Invalid_parameters_fail_closed(
        double readLimit,
        double budgetFraction) {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReadAmplificationBaseBudgetPolicyParameters(
                (decimal)readLimit,
                (decimal)budgetFraction));
    }

    private static ReadAmplificationBaseBudgetPolicySelection Select(
        decimal readLimit,
        decimal budgetFraction,
        params ReadAmplificationBaseBudgetPolicyFact[] facts) =>
        ReadAmplificationBaseBudgetPolicy.Select(
            new ReadAmplificationBaseBudgetPolicyInput(facts),
            new ReadAmplificationBaseBudgetPolicyParameters(
                readLimit,
                budgetFraction));

    private static ReadAmplificationBaseBudgetPolicyFact Insert(
        uint objectId,
        int baseBytes) =>
        ReadAmplificationBaseBudgetPolicyFact.Insert(objectId, baseBytes);

    private static ReadAmplificationBaseBudgetPolicyFact Update(
        uint objectId,
        int baseBytes,
        long historyBytes,
        int deltaBytes) =>
        ReadAmplificationBaseBudgetPolicyFact.Update(
            objectId,
            baseBytes,
            historyBytes,
            deltaBytes);

    private static ReadAmplificationBaseBudgetPolicyFact NoChange(
        uint objectId,
        int baseBytes,
        long historyBytes) =>
        ReadAmplificationBaseBudgetPolicyFact.NoChange(
            objectId,
            baseBytes,
            historyBytes);

    private static void AssertModes(
        ReadAmplificationBaseBudgetPolicySelection selection,
        params (uint ObjectId, ObjectVersionWriteMode Mode)[] expected) {
        Assert.Equal(
            expected.Select(static item => new UpdateRepresentationDecision(
                item.ObjectId,
                item.Mode)),
            selection.UpdateDecisions);
    }
}
