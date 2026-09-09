using Atelia.DurableGraph;

namespace Atelia.ListDeltaReplayProbe;

// Research selectors only. None is a persisted codec identity or a new product configuration.
internal enum FallbackStrategy {
    Position,
    LocalResync,
    BoundedMyers,
    MyersThenLocal,
    LocalThenMyers,
}

internal enum FallbackStopReason {
    PositionOnly,
    LocalCompleted,
    LocalBudgetExhausted,
    MyersSucceeded,
    MyersIncomplete,
    NoMatchesRemaining,
}

internal readonly record struct FallbackDiagnostics(int InitialBudget, int BaselineComparisons,
    int LocalComparisons, int MyersComparisons, int BudgetRemaining, bool Triggered,
    bool MyersAttempted, bool MyersSucceeded, ListDeltaLocalStatus? LocalStatus,
    FallbackStopReason StopReason);

/// <summary>Two bounded research coordinators over the unchanged product matching kernels.</summary>
internal static class FallbackMatcher<TState, TOps> where TState : unmanaged where TOps : IStateOps<TState> {
    internal static List<ListDeltaRange> Plan(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, FallbackStrategy strategy, out FallbackDiagnostics diagnostics) =>
        PlanWithBudget(prior, current, slot, strategy,
            (int)Math.Min(1_000_000L, 4096L + 8L * (prior.Length + (long)current.Length)), out diagnostics);

    // An internal budget override makes reservation and exhaustion boundaries independently testable.
    internal static List<ListDeltaRange> PlanWithBudget(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, FallbackStrategy strategy, int comparisonBudget, out FallbackDiagnostics diagnostics) {
        if (!Enum.IsDefined(strategy)) { throw new ArgumentOutOfRangeException(nameof(strategy)); }
        ArgumentOutOfRangeException.ThrowIfNegative(comparisonBudget);
        List<ListDeltaRange> result = [];
        if (strategy == FallbackStrategy.Position) {
            ListDeltaMatcher<TState, TOps>.AddGap(result, 0, prior.Length, 0, current.Length);
            diagnostics = new(comparisonBudget, 0, 0, 0, comparisonBudget, false, false, false,
                null, FallbackStopReason.PositionOnly);
            return result;
        }

        // Baseline prefix/suffix work is outside the additional-search budget, as in product Plan.
        int baselineComparisons = 0;
        int prefix = 0;
        int common = Math.Min(prior.Length, current.Length);
        while (prefix < common) {
            baselineComparisons++;
            if (!TOps.StateEquals(in prior[prefix], in current[prefix], slot)) { break; }
            prefix++;
        }
        ListDeltaMatcher<TState, TOps>.Add(result, 0, 0, prefix);
        int suffix = 0;
        while (suffix < common - prefix) {
            baselineComparisons++;
            if (!TOps.StateEquals(in prior[prior.Length - suffix - 1], in current[current.Length - suffix - 1], slot)) { break; }
            suffix++;
        }
        int oldEnd = prior.Length - suffix;
        int newEnd = current.Length - suffix;
        int oldIndex = prefix, newIndex = prefix;
        int remaining = comparisonBudget;
        int localComparisons = 0, myersComparisons = 0;
        bool triggered = false, myersAttempted = false, myersSucceeded = false;
        ListDeltaLocalStatus? localStatus = null;
        FallbackStopReason stopReason;

        if (strategy is FallbackStrategy.BoundedMyers or FallbackStrategy.MyersThenLocal) {
            myersAttempted = true;
            int allowance = strategy == FallbackStrategy.MyersThenLocal ? remaining / 2 : remaining;
            myersSucceeded = TryMyers(prior, current, slot, oldIndex, oldEnd, newIndex, newEnd,
                allowance, ref remaining, out myersComparisons, result);
            if (myersSucceeded) {
                stopReason = FallbackStopReason.MyersSucceeded;
            } else if (strategy == FallbackStrategy.BoundedMyers) {
                // Incomplete means only that a bound prevented completion. A false result alone does
                // not identify whether comparison, edit depth, or trace capacity was the first bound.
                ListDeltaMatcher<TState, TOps>.AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
                stopReason = FallbackStopReason.MyersIncomplete;
            } else {
                triggered = true;
                int before = remaining;
                localStatus = ListDeltaMatcher<TState, TOps>.TryLocal(prior, current, slot,
                    ref oldIndex, oldEnd, ref newIndex, newEnd, ref remaining, result);
                localComparisons = before - remaining;
                stopReason = FinishLocal(localStatus.Value, oldIndex, oldEnd, newIndex, newEnd, result);
            }
        } else {
            int before = remaining;
            localStatus = ListDeltaMatcher<TState, TOps>.TryLocal(prior, current, slot,
                ref oldIndex, oldEnd, ref newIndex, newEnd, ref remaining, result,
                stopAtWindowMiss: strategy == FallbackStrategy.LocalThenMyers);
            localComparisons = before - remaining;
            if (localStatus == ListDeltaLocalStatus.WindowMiss &&
                oldEnd - oldIndex <= ListDeltaMatcher<TState, TOps>.LocalLookahead + 1 &&
                newEnd - newIndex <= ListDeltaMatcher<TState, TOps>.LocalLookahead + 1) {
                // A complete failed window checked the entire Cartesian product of these tails,
                // including the initial pair. With no exact pair left, both kernels can only emit
                // this same positional gap; neither Myers nor another Local scan can improve it.
                ListDeltaMatcher<TState, TOps>.AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
                stopReason = FallbackStopReason.NoMatchesRemaining;
            } else if (localStatus == ListDeltaLocalStatus.WindowMiss) {
                triggered = true;
                // This candidate preserves earlier Local choices, including possibly poor duplicate
                // anchors. It repairs only the unresolved tail; no whole-middle quality claim follows.
                if (remaining > 0) {
                    myersAttempted = true;
                    myersSucceeded = TryMyers(prior, current, slot, oldIndex, oldEnd, newIndex, newEnd,
                        remaining / 2, ref remaining, out myersComparisons, result);
                }
                if (myersSucceeded) {
                    stopReason = FallbackStopReason.MyersSucceeded;
                } else {
                    // The window was already fully checked. Consume its failed pair once so that
                    // resuming Local cannot repeat that search or trigger a second Myers attempt.
                    ListDeltaMatcher<TState, TOps>.Add(result, oldIndex++, newIndex++, 1);
                    before = remaining;
                    localStatus = ListDeltaMatcher<TState, TOps>.TryLocal(prior, current, slot,
                        ref oldIndex, oldEnd, ref newIndex, newEnd, ref remaining, result);
                    localComparisons += before - remaining;
                    stopReason = FinishLocal(localStatus.Value, oldIndex, oldEnd, newIndex, newEnd, result);
                }
            } else {
                stopReason = FinishLocal(localStatus.Value, oldIndex, oldEnd, newIndex, newEnd, result);
            }
        }

        ListDeltaMatcher<TState, TOps>.Add(result, oldEnd, newEnd, suffix);
        diagnostics = new(comparisonBudget, baselineComparisons, localComparisons, myersComparisons,
            remaining, triggered, myersAttempted, myersSucceeded, localStatus, stopReason);
        return result;
    }

    private static bool TryMyers(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current, DurableFieldInfo slot,
        int oldStart, int oldEnd, int newStart, int newEnd, int allowance, ref int remaining,
        out int comparisons, List<ListDeltaRange> result) {
        int myersRemaining = allowance;
        bool success = ListDeltaMatcher<TState, TOps>.Myers(prior[oldStart..oldEnd], current[newStart..newEnd],
            slot, oldStart, newStart, ref myersRemaining, ListDeltaMatcher<TState, TOps>.MaximumMyersDepth,
            ListDeltaMatcher<TState, TOps>.MaximumTraceBytes, result);
        comparisons = allowance - myersRemaining;
        // A reserved allowance is not a charge: unused Myers comparisons remain available to Local.
        remaining -= comparisons;
        return success;
    }

    private static FallbackStopReason FinishLocal(ListDeltaLocalStatus status, int oldIndex, int oldEnd,
        int newIndex, int newEnd, List<ListDeltaRange> result) {
        if (status == ListDeltaLocalStatus.BudgetExhausted) {
            ListDeltaMatcher<TState, TOps>.AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
            return FallbackStopReason.LocalBudgetExhausted;
        }
        if (status != ListDeltaLocalStatus.Complete) {
            throw new InvalidOperationException("An unhandled Local window miss cannot finish a fallback plan.");
        }
        return FallbackStopReason.LocalCompleted;
    }
}
