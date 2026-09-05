namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ReadAmplificationBaseBudgetPolicyTests {
    [Fact]
    public void Complete_example_is_canonical_for_every_input_permutation() {
        ObjectSaveEstimate[] objects = [
            Insert(10, 20), Update(20, 100, 10, 350), Cold(30, 80, 400),
            Update(40, 30, 40, 0), Cold(50, 170, 170),
        ];

        foreach (ObjectSaveEstimate[] permutation in Permutations(objects)) {
            ObjectSaveEstimate[] before = permutation.ToArray();
            AssertWrites(Plan(permutation, 3, 25), Base(10), Delta(20), Base(30), Base(40));
            Assert.Equal(before, permutation);
        }
    }

    [Theory]
    [InlineData(28, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void Update_motive_depends_on_H_plus_D_and_requires_strict_threshold(long history, bool motivated) {
        AssertWrites(Plan([Update(1, 10, 1, history)], 3, 100), motivated ? Base(1) : Delta(1));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void NoChange_motive_depends_on_H_and_requires_strict_threshold(long history, bool motivated) {
        ObjectRepresentationPlan plan = Plan([Cold(1, 10, history)], 3, 100);
        AssertWrites(plan, motivated ? [Base(1)] : []);
    }

    [Fact]
    public void Mandatory_bases_do_not_spend_optional_budget_but_all_live_bases_contribute_to_it() {
        // G=200, Q=20; the Insert and B=D Update are free, so both optional objects fit.
        AssertWrites(Plan([
            Insert(1, 90), Update(2, 90, 90, 0),
            Cold(3, 10, 50), Cold(4, 10, 40),
        ], 3, 10), Base(1), Base(2), Base(3), Base(4));
    }

    [Fact]
    public void Full_base_cost_is_charged_for_Update_not_base_minus_delta() {
        // G=44, Q=11. Charging B-D=1 would incorrectly admit the second candidate too.
        AssertWrites(Plan([
            Update(1, 10, 9, 41), Cold(2, 10, 40), Cold(3, 24, 24),
        ], 3, 25), Base(1));
    }

    [Fact]
    public void Update_and_NoChange_share_ratio_order_and_ObjectId_breaks_ties() {
        // Both candidates have A=4, G=40, Q=10; lower ID wins across change kinds.
        AssertWrites(Plan([
            Update(20, 10, 1, 39), Cold(10, 10, 40), Cold(30, 20, 20),
        ], 3, 25), Base(10), Delta(20));
    }

    [Fact]
    public void Nonfitting_middle_candidate_stops_prefix_without_backfill() {
        // G=100, Q=25. Candidate 2 cannot follow candidate 1; candidate 3 must stay omitted.
        AssertWrites(Plan([
            Cold(1, 10, 100), Cold(2, 20, 180), Cold(3, 5, 40), Cold(4, 65, 65),
        ], 3, 25), Base(1));
    }

    [Fact]
    public void Integer_percent_budget_uses_floor_and_accepts_exact_fit() {
        // G=19, floor(19*89/100)=16, not 17.
        AssertWrites(Plan([
            Cold(1, 16, 64), Cold(2, 1, 3), Insert(3, 2),
        ], 2, 89), Base(1), Base(3));
    }

    [Fact]
    public void First_candidate_can_exceed_budget_and_then_selection_stops() {
        AssertWrites(Plan([
            Cold(1, 80, 800), Cold(2, 20, 100),
        ], 3, 25), Base(1));
    }

    [Fact]
    public void First_candidate_exception_applies_even_when_budget_rounds_to_zero() {
        AssertWrites(Plan([Cold(1, 1, 2)], 1, 1), Base(1));
    }

    [Fact]
    public void Selected_zero_cost_candidate_uses_first_position_so_next_candidate_cannot_overshoot() {
        AssertWrites(Plan([
            Cold(1, 0, 1), Cold(2, 10, 100), Cold(3, 90, 90),
        ], 3, 1), Base(1));
    }

    [Fact]
    public void Known_zero_estimates_preserve_change_kind_and_zero_denominator_semantics() {
        AssertWrites(Plan([
            Cold(1, 0, 0), Cold(2, 0, 1), Update(3, 0, 0, 0),
            Insert(4, 0), Update(5, 5, 0, 0), Cold(6, 5, 0),
        ], 1, 100), Base(2), Base(3), Base(4), Delta(5));
    }

    [Fact]
    public void Base_smaller_than_delta_is_selected_without_a_read_motive() {
        AssertWrites(Plan([Update(1, 1, 2, 0)], int.MaxValue, 1), Base(1));
    }

    [Fact]
    public void Large_near_equal_ratios_are_sorted_exactly_without_cross_product_overflow() {
        long baseBytes = long.MaxValue / 2;
        // A1=2; A2=2+1/B. Floating point collapses this difference and would pick lower ID 1.
        AssertWrites(Plan([
            Cold(1, baseBytes, long.MaxValue - 1), Cold(2, baseBytes, long.MaxValue),
        ], 1, 25), Base(2));
    }

    [Fact]
    public void Maximum_integer_threshold_preserves_equality_and_strictness() {
        long equality = 2L * int.MaxValue;
        AssertWrites(Plan([
            Cold(1, 2, equality), Cold(2, 2, equality + 1),
            Update(3, 2, 1, equality - 1), Update(4, 2, 1, equality),
        ], int.MaxValue, 100), Base(2), Delta(3), Base(4));
    }

    [Fact]
    public void Large_threshold_product_and_full_percent_budget_do_not_overflow() {
        AssertWrites(Plan([Update(1, long.MaxValue, 0, long.MaxValue)], int.MaxValue, 100), Delta(1));
    }

    [Fact]
    public void Maximum_total_base_bytes_can_form_a_budget_and_optional_prefix() {
        long firstBase = long.MaxValue / 2;
        long secondBase = long.MaxValue - firstBase;
        // G is exactly long.MaxValue. Q=floor(G/2)=firstBase, so only object 1 fits.
        AssertWrites(Plan([
            Cold(1, firstBase, long.MaxValue), Cold(2, secondBase, long.MaxValue),
        ], 1, 50), Base(1));
    }

    [Fact]
    public void Maximum_reconstruction_sum_is_valid() {
        AssertWrites(Plan([Update(1, 2, 1, long.MaxValue - 1)], 1, 100), Base(1));
    }

    [Fact]
    public void Aggregate_base_overflow_is_rejected() {
        Assert.Throws<OverflowException>(() => Plan([Insert(1, long.MaxValue), Insert(2, 1)]));
    }

    [Fact]
    public void Reconstruction_overflow_is_rejected_even_for_dominant_Update() {
        Assert.Throws<OverflowException>(() => Plan([Update(1, 1, 1, long.MaxValue)]));
    }

    [Fact]
    public void All_rows_are_validated_even_after_an_oversized_first_candidate() {
        Assert.Throws<OverflowException>(() => Plan([
            Cold(1, 100, 1000), Update(2, 1, 1, long.MaxValue),
        ], 3, 1));
    }

    [Fact]
    public void Null_input_is_rejected_and_empty_input_is_valid() {
        Assert.Throws<ArgumentNullException>(() => Plan(null!));
        AssertWrites(Plan([]));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(int.MinValue, 25)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 101)]
    [InlineData(3, int.MaxValue)]
    [InlineData(0, 0)]
    public void Invalid_parameters_are_rejected_even_for_empty_input(int limit, int percent) {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([], limit, percent));
    }

    [Fact]
    public void Zero_duplicate_and_default_ObjectIds_are_rejected() {
        Assert.Throws<ArgumentException>(() => Plan([Insert(0, 1)]));
        Assert.Throws<ArgumentException>(() => Plan([Insert(1, 1), Cold(1, 1, 1)]));
        Assert.Throws<ArgumentException>(() => Plan([default]));
    }

    [Fact]
    public void Unknown_change_kind_is_rejected() {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([
            new(1, (ObjectSaveChangeKind)int.MaxValue, 1, null, null),
        ]));
    }

    [Fact]
    public void Negative_applicable_estimates_are_rejected_for_every_field_and_kind() {
        ObjectSaveEstimate[] invalid = [
            Insert(1, -1), Update(1, -1, 0, 0), Cold(1, -1, 0),
            Update(1, 1, -1, 0), Update(1, 1, 0, -1), Cold(1, 1, -1),
        ];
        foreach (ObjectSaveEstimate row in invalid) {
            Assert.Throws<ArgumentOutOfRangeException>(() => Plan([row]));
        }
    }

    [Fact]
    public void Every_inapplicable_or_missing_nullable_field_shape_is_rejected() {
        ObjectSaveEstimate[] invalid = [
            new(1, ObjectSaveChangeKind.Insert, 1, 0, null),
            new(1, ObjectSaveChangeKind.Insert, 1, null, 0),
            new(1, ObjectSaveChangeKind.Insert, 1, 0, 0),
            new(1, ObjectSaveChangeKind.Update, 1, null, 0),
            new(1, ObjectSaveChangeKind.Update, 1, 0, null),
            new(1, ObjectSaveChangeKind.Update, 1, null, null),
            new(1, ObjectSaveChangeKind.NoChange, 1, 0, 0),
            new(1, ObjectSaveChangeKind.NoChange, 1, null, null),
            new(1, ObjectSaveChangeKind.NoChange, 1, 0, null),
        ];
        foreach (ObjectSaveEstimate row in invalid) {
            Assert.Throws<ArgumentException>(() => Plan([row]));
        }
    }

    [Fact]
    public void Failure_does_not_mutate_input() {
        List<ObjectSaveEstimate> objects = [Cold(9, 10, 100), Insert(2, -1)];
        ObjectSaveEstimate[] before = objects.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(objects));
        Assert.Equal(before, objects);
    }

    [Fact]
    public void Repeated_calls_have_no_budget_state_and_results_own_their_readonly_collection() {
        List<ObjectSaveEstimate> objects = [Cold(2, 20, 100), Cold(1, 80, 800)];
        ObjectRepresentationPlan first = Plan(objects);
        AssertWrites(first, Base(1));
        AssertWrites(Plan(objects), Base(1));
        objects.Clear();
        objects.Add(Insert(99, 1));
        AssertWrites(first, Base(1));
        AssertWrites(Plan(objects), Base(99));

        // A direct array exposed as IReadOnlyList would allow the following cast and mutation.
        IList<ObjectWriteDecision> writableView = Assert.IsAssignableFrom<IList<ObjectWriteDecision>>(first.Writes);
        Assert.True(writableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => writableView[0] = Delta(99));
        Assert.Throws<NotSupportedException>(() => writableView.Clear());
        AssertWrites(first, Base(1));
    }

    private static ObjectRepresentationPlan Plan(
        IReadOnlyList<ObjectSaveEstimate> objects,
        int limit = 3,
        int percent = 25) => ReadAmplificationBaseBudgetPolicy.Plan(objects, new(limit, percent));

    private static ObjectSaveEstimate Insert(uint id, long baseBytes) =>
        new(id, ObjectSaveChangeKind.Insert, baseBytes, null, null);

    private static ObjectSaveEstimate Update(uint id, long baseBytes, long deltaBytes, long historyBytes) =>
        new(id, ObjectSaveChangeKind.Update, baseBytes, deltaBytes, historyBytes);

    private static ObjectSaveEstimate Cold(uint id, long baseBytes, long historyBytes) =>
        new(id, ObjectSaveChangeKind.NoChange, baseBytes, null, historyBytes);

    private static ObjectWriteDecision Base(uint id) => new(id, ObjectRepresentationMode.Base);

    private static ObjectWriteDecision Delta(uint id) => new(id, ObjectRepresentationMode.Delta);

    private static void AssertWrites(ObjectRepresentationPlan plan, params ObjectWriteDecision[] expected) =>
        Assert.Equal(expected, plan.Writes);

    private static IEnumerable<ObjectSaveEstimate[]> Permutations(ObjectSaveEstimate[] objects) {
        if (objects.Length == 0) {
            yield return [];
            yield break;
        }
        for (int index = 0; index < objects.Length; index++) {
            ObjectSaveEstimate[] rest = objects.Where((_, other) => other != index).ToArray();
            foreach (ObjectSaveEstimate[] tail in Permutations(rest)) {
                yield return [objects[index], .. tail];
            }
        }
    }
}
