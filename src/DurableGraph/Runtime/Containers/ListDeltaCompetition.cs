using Atelia.DurableGraph.Schema;
using Atelia.DurableGraph.Serialization;

namespace Atelia.DurableGraph.Runtime;

// Internal evidence for tests and replay; never persisted or used as a selection input.
internal enum ListDeltaCompetitionOutcome {
    NoChange, ExplicitPlan, NotTriggered, MyersIncomplete, SamePlan, ByteLimitReached, MyersWon,
}

internal readonly record struct ListDeltaCompetitionObservation(ListDeltaCompetitionOutcome Outcome,
    int IncumbentBytes, int ChallengerWrittenBytes, int SearchBudgetPerMatcher) {
    internal bool Triggered => Outcome is ListDeltaCompetitionOutcome.MyersIncomplete or
        ListDeltaCompetitionOutcome.SamePlan or ListDeltaCompetitionOutcome.ByteLimitReached or
        ListDeltaCompetitionOutcome.MyersWon;
}

/// <summary>Competes complete plans by actual body bytes while retaining an independently prepared Local incumbent.</summary>
internal static class ListDeltaCompetition<TState, TOps> where TState : unmanaged where TOps : IStateOps<TState> {
    // Called only after the exact sequence NoChange check. Each matcher receives its own original budget.
    internal static PreparedDeltaBody PrepareChanged(FrozenListState<TState> prior, FrozenListState<TState> current,
        ListLayout layout, out ListDeltaCompetitionObservation observation) {
        List<ListDeltaRange> local = ListDeltaMatcher<TState, TOps>.PlanLocal(
            prior.Elements, current.Elements, layout.ElementSlot, out bool stalled);
        ListStateBody<TState, TOps>.TryEncodePlan(prior, current, layout, local, null, out PreparedDeltaBody? incumbent);
        int ceiling = incumbent!.Body.Length;
        int budget = ListDeltaMatcher<TState, TOps>.GetComparisonBudget(prior.Count, current.Count);
        observation = new(ListDeltaCompetitionOutcome.NotTriggered, ceiling, 0, budget);
        if (!stalled) { return incumbent; }

        if (!ListDeltaMatcher<TState, TOps>.TryPlanMyers(prior.Elements, current.Elements, layout.ElementSlot,
                out List<ListDeltaRange>? challenger)) {
            observation = observation with { Outcome = ListDeltaCompetitionOutcome.MyersIncomplete };
            return incumbent;
        }
        if (local.SequenceEqual(challenger)) {
            observation = observation with { Outcome = ListDeltaCompetitionOutcome.SamePlan };
            return incumbent;
        }
        // The child codec remains atomic. This limits candidate output at element boundaries, not peak allocation.
        bool won = ListStateBody<TState, TOps>.TryEncodePlan(prior, current, layout, challenger, ceiling,
            out PreparedDeltaBody? result, out int writtenBytes);
        observation = observation with {
            Outcome = won ? ListDeltaCompetitionOutcome.MyersWon : ListDeltaCompetitionOutcome.ByteLimitReached,
            ChallengerWrittenBytes = writtenBytes,
        };
        return won ? result! : incumbent;
    }
}
