using Atelia.DurableGraph.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Atelia.DurableGraph.Runtime;

/// <summary>A consecutive target range paired with prior contents, or new contents when OldStart is -1.</summary>
internal readonly record struct ListDeltaRange(int OldStart, int NewStart, int Count);

/// <summary>Why the resumable local search stopped; only Complete consumes the terminal gap.</summary>
internal enum ListDeltaLocalStatus { Complete, WindowMiss, BudgetExhausted }

/// <summary>Finds reusable coordinates without encoding elements or invoking their Delta preparation.</summary>
internal static class ListDeltaMatcher<TState, TOps> where TState : unmanaged where TOps : IStateOps<TState> {
    internal const int LocalLookahead = 32;
    internal const int MaximumMyersDepth = 128;
    internal const int MaximumTraceBytes = 1024 * 1024;

    internal static List<ListDeltaRange> Plan(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, ListDeltaAlgorithm algorithm) =>
        PlanWithBudget(prior, current, slot, algorithm,
            GetComparisonBudget(prior.Length, current.Length),
            MaximumMyersDepth, MaximumTraceBytes);

    internal static int GetComparisonBudget(int priorCount, int currentCount) =>
        (int)Math.Min(1_000_000L, 4096L + 8L * (priorCount + (long)currentCount));

    // Complete the original Local search while observing stalls, without adding or reordering comparisons.
    internal static List<ListDeltaRange> PlanLocal(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, out bool stalled) =>
        PlanLocalWithBudget(prior, current, slot, GetComparisonBudget(prior.Length, current.Length), out stalled);

    internal static List<ListDeltaRange> PlanLocalWithBudget(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, int comparisonBudget, out bool stalled) {
        ArgumentOutOfRangeException.ThrowIfNegative(comparisonBudget);
        List<ListDeltaRange> result = [];
        TrimCommonEnds(prior, current, slot, result, out int prefix, out int oldEnd, out int newEnd, out int suffix);
        int oldIndex = prefix, newIndex = prefix;
        stalled = false;
        while (true) {
            ListDeltaLocalStatus status = TryLocal(prior, current, slot, ref oldIndex, oldEnd,
                ref newIndex, newEnd, ref comparisonBudget, result, stopAtWindowMiss: true);
            if (status == ListDeltaLocalStatus.Complete) { break; }
            if (status == ListDeltaLocalStatus.BudgetExhausted) {
                // TryLocal reports exhaustion only while both middle spans still contain elements.
                stalled = true;
                AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
                break;
            }
            stalled |= oldEnd - oldIndex > LocalLookahead + 1 || newEnd - newIndex > LocalLookahead + 1;
            // This pair and its entire window were already compared. Keep Local's original fallback pair.
            // Even a proven short-tail miss continues normally to preserve Local's exact comparison work.
            Add(result, oldIndex++, newIndex++, 1);
        }
        Add(result, oldEnd, newEnd, suffix);
        return result;
    }

    // Unlike explicit BoundedMyers, competition must distinguish completion from positional fallback.
    internal static bool TryPlanMyers(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, [NotNullWhen(true)] out List<ListDeltaRange>? plan) =>
        TryPlanMyersWithBudget(prior, current, slot, GetComparisonBudget(prior.Length, current.Length), out plan);

    internal static bool TryPlanMyersWithBudget(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, int comparisonBudget, [NotNullWhen(true)] out List<ListDeltaRange>? plan,
        int maximumEditDepth = MaximumMyersDepth, int maximumTraceBytes = MaximumTraceBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(comparisonBudget);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEditDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTraceBytes);
        List<ListDeltaRange> result = [];
        TrimCommonEnds(prior, current, slot, result, out int prefix, out int oldEnd, out int newEnd, out int suffix);
        if (!Myers(prior[prefix..oldEnd], current[prefix..newEnd], slot, prefix, prefix,
                ref comparisonBudget, maximumEditDepth, maximumTraceBytes, result)) {
            plan = null;
            return false;
        }
        Add(result, oldEnd, newEnd, suffix);
        plan = result;
        return true;
    }

    // Internal overrides make deterministic budget boundaries observable without a public tuning surface.
    internal static List<ListDeltaRange> PlanWithBudget(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, ListDeltaAlgorithm algorithm, int comparisonBudget,
        int maximumEditDepth = MaximumMyersDepth, int maximumTraceBytes = MaximumTraceBytes) {
        // Adaptive chooses by encoded bytes and therefore cannot be dispatched as a pure matcher.
        if (algorithm is not (ListDeltaAlgorithm.Position or ListDeltaAlgorithm.LocalResync or ListDeltaAlgorithm.BoundedMyers)) {
            throw new ArgumentOutOfRangeException(nameof(algorithm));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(comparisonBudget);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEditDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTraceBytes);
        List<ListDeltaRange> result = [];
        if (algorithm == ListDeltaAlgorithm.Position) {
            AddGap(result, 0, prior.Length, 0, current.Length);
            return result;
        }

        TrimCommonEnds(prior, current, slot, result, out int prefix, out int oldEnd, out int newEnd, out int suffix);
        if (algorithm == ListDeltaAlgorithm.LocalResync) {
            int oldIndex = prefix, newIndex = prefix;
            if (TryLocal(prior, current, slot, ref oldIndex, oldEnd, ref newIndex, newEnd,
                    ref comparisonBudget, result) != ListDeltaLocalStatus.Complete) {
                AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
            }
        } else if (!Myers(prior[prefix..oldEnd], current[prefix..newEnd], slot, prefix, prefix,
                ref comparisonBudget, maximumEditDepth, maximumTraceBytes, result)) {
            AddGap(result, prefix, oldEnd, prefix, newEnd);
        }
        Add(result, oldEnd, newEnd, suffix);
        return result;
    }

    private static void TrimCommonEnds(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current, DurableFieldInfo slot,
        List<ListDeltaRange> result, out int prefix, out int oldEnd, out int newEnd, out int suffix) {
        prefix = 0;
        int common = Math.Min(prior.Length, current.Length);
        while (prefix < common && TOps.StateEquals(in prior[prefix], in current[prefix], slot)) { prefix++; }
        Add(result, 0, 0, prefix);
        suffix = 0;
        while (suffix < common - prefix &&
            TOps.StateEquals(in prior[prior.Length - suffix - 1], in current[current.Length - suffix - 1], slot)) {
            suffix++;
        }
        oldEnd = prior.Length - suffix;
        newEnd = current.Length - suffix;
    }

    // Internal kernels allow bounded matcher experiments to share the production comparison and tie rules.
    // A miss leaves its pair unconsumed; a caller can append that pair before resuming to avoid re-searching it.
    internal static ListDeltaLocalStatus TryLocal(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current,
        DurableFieldInfo slot, ref int oldIndex, int oldEnd, ref int newIndex, int newEnd,
        ref int remaining, List<ListDeltaRange> result, bool stopAtWindowMiss = false) {
        while (oldIndex < oldEnd && newIndex < newEnd) {
            if (remaining == 0) { return ListDeltaLocalStatus.BudgetExhausted; }
            remaining--;
            if (TOps.StateEquals(in prior[oldIndex], in current[newIndex], slot)) {
                Add(result, oldIndex++, newIndex++, 1);
                continue;
            }
            bool found = false;
            int oldOffset = 0, newOffset = 0;
            int maxOld = Math.Min(LocalLookahead, oldEnd - oldIndex - 1);
            int maxNew = Math.Min(LocalLookahead, newEnd - newIndex - 1);
            // This enumeration fixes the duplicate-value tie: (sum, old offset, new offset).
            for (int sum = 1; sum <= maxOld + maxNew && !found; sum++) {
                for (int candidateOld = Math.Max(0, sum - maxNew); candidateOld <= Math.Min(maxOld, sum); candidateOld++) {
                    int candidateNew = sum - candidateOld;
                    if (remaining == 0) { return ListDeltaLocalStatus.BudgetExhausted; }
                    remaining--;
                    if (!TOps.StateEquals(in prior[oldIndex + candidateOld], in current[newIndex + candidateNew], slot)) { continue; }
                    oldOffset = candidateOld;
                    newOffset = candidateNew;
                    found = true;
                    break;
                }
            }
            if (found) {
                AddGap(result, oldIndex, oldIndex + oldOffset, newIndex, newIndex + newOffset);
                oldIndex += oldOffset;
                newIndex += newOffset;
                Add(result, oldIndex++, newIndex++, 1); // The anchor is already known equal.
            } else {
                if (stopAtWindowMiss) { return ListDeltaLocalStatus.WindowMiss; }
                Add(result, oldIndex++, newIndex++, 1);
            }
        }
        AddGap(result, oldIndex, oldEnd, newIndex, newEnd);
        oldIndex = oldEnd;
        newIndex = newEnd;
        return ListDeltaLocalStatus.Complete;
    }

    // The input spans are local slices; output coordinates use independent offsets. Failure never changes result.
    internal static bool Myers(ReadOnlySpan<TState> prior, ReadOnlySpan<TState> current, DurableFieldInfo slot,
        int oldOffset, int newOffset, ref int remaining, int maximumDepth, int maximumTraceBytes,
        List<ListDeltaRange> result) {
        if (prior.IsEmpty || current.IsEmpty) {
            AddGap(result, oldOffset, oldOffset + prior.Length, newOffset, newOffset + current.Length);
            return true;
        }
        if (Math.Abs((long)prior.Length - current.Length) > maximumDepth) { return false; }
        int limit = (int)Math.Min(maximumDepth, prior.Length + (long)current.Length);
        List<int[]> trace = [];
        long traceBytes = 0;
        for (int depth = 0; depth <= limit; depth++) {
            // Each active diagonal has one furthest-x coordinate. No input-sized frontier copies.
            traceBytes += ((long)depth + 1) * sizeof(int);
            if (traceBytes > maximumTraceBytes) { return false; }
            int[] frontier = new int[depth + 1];
            Array.Fill(frontier, -1);
            int[]? previous = depth == 0 ? null : trace[depth - 1];
            for (int diagonalIndex = 0; diagonalIndex <= depth; diagonalIndex++) {
                int diagonal = -depth + 2 * diagonalIndex;
                int x;
                if (depth == 0) {
                    x = 0;
                } else {
                    bool insert = SelectInsertion(previous!, diagonalIndex, depth);
                    int previousX = insert ? previous![diagonalIndex] : previous![diagonalIndex - 1];
                    if (previousX < 0) { continue; }
                    x = previousX + (insert ? 0 : 1);
                }
                int y = x - diagonal;
                if (x > prior.Length || y < 0 || y > current.Length) { continue; }
                while (x < prior.Length && y < current.Length) {
                    if (remaining == 0) { return false; }
                    remaining--;
                    if (!TOps.StateEquals(in prior[x], in current[y], slot)) { break; }
                    x++;
                    y++;
                }
                frontier[diagonalIndex] = x;
                if (x == prior.Length && y == current.Length) {
                    trace.Add(frontier);
                    List<ListDeltaRange> matches = Recover(trace, prior.Length, current.Length, depth, oldOffset, newOffset);
                    int oldIndex = oldOffset, newIndex = newOffset;
                    foreach (ListDeltaRange match in matches) {
                        AddGap(result, oldIndex, match.OldStart, newIndex, match.NewStart);
                        Add(result, match.OldStart, match.NewStart, match.Count);
                        oldIndex = match.OldStart + match.Count;
                        newIndex = match.NewStart + match.Count;
                    }
                    AddGap(result, oldIndex, oldOffset + prior.Length, newIndex, newOffset + current.Length);
                    return true;
                }
            }
            trace.Add(frontier);
        }
        return false;
    }

    private static bool SelectInsertion(int[] previous, int index, int depth) =>
        index == 0 || (index != depth && previous[index - 1] < previous[index]);

    private static List<ListDeltaRange> Recover(List<int[]> trace, int oldLength, int newLength, int depth,
        int oldOffset, int newOffset) {
        List<ListDeltaRange> reverse = [];
        int x = oldLength, y = newLength;
        for (int d = depth; d > 0; d--) {
            int diagonal = x - y;
            int index = (diagonal + d) / 2;
            int[] previous = trace[d - 1];
            bool insert = SelectInsertion(previous, index, d);
            int previousDiagonal = diagonal + (insert ? 1 : -1);
            int previousX = previous[(previousDiagonal + d - 1) / 2];
            int previousY = previousX - previousDiagonal;
            int snakeX = previousX + (insert ? 0 : 1);
            int snakeY = previousY + (insert ? 1 : 0);
            if (x > snakeX) { reverse.Add(new(oldOffset + snakeX, newOffset + snakeY, x - snakeX)); }
            x = previousX;
            y = previousY;
        }
        if (x > 0) { reverse.Add(new(oldOffset, newOffset, x)); }
        reverse.Reverse();
        return reverse;
    }

    internal static void AddGap(List<ListDeltaRange> result, int oldStart, int oldEnd, int newStart, int newEnd) {
        int common = Math.Min(oldEnd - oldStart, newEnd - newStart);
        Add(result, oldStart, newStart, common);
        Add(result, -1, newStart + common, newEnd - newStart - common);
    }

    internal static void Add(List<ListDeltaRange> result, int oldStart, int newStart, int count) {
        if (count == 0) { return; }
        if (result.Count > 0) {
            ListDeltaRange previous = result[^1];
            if (previous.NewStart + previous.Count == newStart &&
                ((previous.OldStart == -1 && oldStart == -1) ||
                    (previous.OldStart >= 0 && oldStart >= 0 && previous.OldStart + previous.Count == oldStart))) {
                result[^1] = previous with { Count = checked(previous.Count + count) };
                return;
            }
        }
        result.Add(new(oldStart, newStart, count));
    }
}
