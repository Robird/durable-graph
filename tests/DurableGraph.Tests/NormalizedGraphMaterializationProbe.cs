using System.Runtime.CompilerServices;

namespace Atelia.DurableGraph.Tests;

internal enum ProbeMaterializationPhase {
    Allocation,
    Hydration,
}

internal delegate ProbeNode ProbePlaceholderAllocator();

internal static class NormalizedGraphMaterializationProbe {
    internal static ProbeNode Materialize(
        NormalizedBaselineGraph baseline,
        Action<ProbeMaterializationPhase, ProbeId>? phaseHook = null,
        ProbePlaceholderAllocator? allocator = null) {
        ArgumentNullException.ThrowIfNull(baseline);
        allocator ??= AllocateUninitializedPlaceholder;

        ProbeId[] currentReachableIds = FindCurrentReachableIds(baseline)
            .ToArray();
        Dictionary<ProbeId, ProbeNode> placeholders = [];

        foreach (ProbeId id in currentReachableIds) {
            try {
                ProbeNode placeholder = allocator();
                if (placeholder is null ||
                    placeholder.ConstructorWasRun ||
                    placeholder.Id.Value > 0) {
                    throw new InvalidOperationException(
                        "The probe allocator must return an uninitialized, unbound node.");
                }

                placeholders.Add(id, placeholder);
                phaseHook?.Invoke(ProbeMaterializationPhase.Allocation, id);
            } catch (Exception exception) {
                throw new ProbeGraphMaterializationException(
                    ProbeMaterializationPhase.Allocation,
                    id,
                    exception);
            }
        }

        foreach (ProbeId id in currentReachableIds) {
            try {
                phaseHook?.Invoke(ProbeMaterializationPhase.Hydration, id);

                ProbeSnapshot snapshot = baseline.Entries[id].Snapshot;
                ProbeNode? next = ResolveReference(snapshot.NextId, placeholders);
                ProbeNode? alias = ResolveReference(snapshot.AliasId, placeholders);
                placeholders[id].HydrateForMaterialization(
                    id,
                    in snapshot,
                    next,
                    alias);
            } catch (Exception exception) {
                throw new ProbeGraphMaterializationException(
                    ProbeMaterializationPhase.Hydration,
                    id,
                    exception);
            }
        }

        return placeholders[baseline.RootId];
    }

    private static ProbeNode AllocateUninitializedPlaceholder() {
        return (ProbeNode)RuntimeHelpers.GetUninitializedObject(typeof(ProbeNode));
    }

    private static HashSet<ProbeId> FindCurrentReachableIds(
        NormalizedBaselineGraph baseline) {
        Stack<ProbeId> pending = new();
        HashSet<ProbeId> reachableIds = [];
        pending.Push(baseline.RootId);

        while (pending.Count > 0) {
            ProbeId id = pending.Pop();
            if (!reachableIds.Add(id)) {
                continue;
            }

            ProbeSnapshot snapshot = baseline.Entries[id].Snapshot;
            if (snapshot.AliasId is ProbeId aliasId) {
                pending.Push(aliasId);
            }

            if (snapshot.NextId is ProbeId nextId) {
                pending.Push(nextId);
            }
        }

        return reachableIds;
    }

    private static ProbeNode? ResolveReference(
        ProbeId? referenceId,
        IReadOnlyDictionary<ProbeId, ProbeNode> placeholders) {
        return referenceId is ProbeId id ? placeholders[id] : null;
    }
}

internal sealed class ProbeGraphMaterializationException : InvalidOperationException {
    internal ProbeGraphMaterializationException(
        ProbeMaterializationPhase phase,
        ProbeId id,
        Exception innerException)
        : base($"Probe graph {phase} failed for identity {id}.", innerException) {
        Phase = phase;
        Id = id;
    }

    internal ProbeMaterializationPhase Phase { get; }

    internal ProbeId Id { get; }
}
