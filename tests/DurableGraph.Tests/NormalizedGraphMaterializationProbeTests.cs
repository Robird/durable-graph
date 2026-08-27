using System.Runtime.CompilerServices;

namespace Atelia.DurableGraph.Tests;

public sealed class NormalizedGraphMaterializationProbeTests {
    [Fact]
    public void NormalizedHistoricalGraphMaterializesExactCurrentRootClosureAndFeedsR1Save() {
        StoredGraphImage image = new(
            Id(1),
            [
                new StoredGraphRecordEntry(
                    Id(99),
                    new StoredProbeRecordV2(
                        StoredGraphNormalizationProbe.SchemaV2,
                        Snapshot(99, nextId: 99))),
                new StoredGraphRecordEntry(
                    Id(3),
                    new StoredProbeRecordV2(
                        StoredGraphNormalizationProbe.SchemaV2,
                        Snapshot(30))),
                new StoredGraphRecordEntry(
                    Id(2),
                    new StoredProbeRecordV2(
                        StoredGraphNormalizationProbe.SchemaV2,
                        Snapshot(20, nextId: 1, aliasId: 3))),
                new StoredGraphRecordEntry(
                    Id(1),
                    new StoredProbeRecordV1(
                        StoredGraphNormalizationProbe.SchemaV1,
                        new ProbeSnapshotV1(10, Id(2)))),
            ]);
        NormalizedBaselineGraph baseline =
            StoredGraphNormalizationProbe.LoadNormalized(
                image,
                (in ProbeSnapshotV1 oldValue, out ProbeSnapshot newValue) =>
                    newValue = new ProbeSnapshot(
                        oldValue.Value,
                        oldValue.NextId,
                        oldValue.NextId));
        List<PhaseEvent> events = [];

        ProbeNode root = NormalizedGraphMaterializationProbe.Materialize(
            baseline,
            (phase, id) => events.Add(new PhaseEvent(phase, id)));

        AssertTwoPass(events, 1, 2, 3);
        Assert.Equal(Id(1), root.Id);
        Assert.Equal(10, root.Value);
        Assert.NotNull(root.Next);
        Assert.Same(root.Next, root.Alias);
        ProbeNode child = root.Next;
        Assert.Equal(Id(2), child.Id);
        Assert.Equal(20, child.Value);
        Assert.Same(root, child.Next);
        Assert.NotNull(child.Alias);
        ProbeNode leaf = child.Alias;
        Assert.Equal(Id(3), leaf.Id);
        Assert.Equal(30, leaf.Value);
        Assert.Null(leaf.Next);
        Assert.Null(leaf.Alias);

        Assert.Equal(Id(1), baseline.RootId);
        Assert.Equal(4, baseline.Entries.Count);
        Assert.Equal(
            new BaselineEntry(Snapshot(10, nextId: 2, aliasId: 2), true),
            baseline.Entries[Id(1)]);
        Assert.Equal(
            new BaselineEntry(Snapshot(20, nextId: 1, aliasId: 3), false),
            baseline.Entries[Id(2)]);
        Assert.Equal(
            new BaselineEntry(Snapshot(30), false),
            baseline.Entries[Id(3)]);
        Assert.Equal(
            new BaselineEntry(Snapshot(99, nextId: 99), false),
            baseline.Entries[Id(99)]);

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Equal(Id(1), delta.ResultRootId);
        AssertIdSet(delta.Upserts.Keys, 1);
        Assert.Equal(Snapshot(10, nextId: 2, aliasId: 2), delta.Upserts[Id(1)]);
        AssertIdSet(delta.UnreachableIds, 99);
        Assert.True(baseline.Entries[Id(1)].RequiresRewrite);
    }

    [Fact]
    public void SelfCycleRestoresReferenceIdentity() {
        NormalizedBaselineGraph baseline = Baseline(
            rootId: 1,
            (1, 10, 1, null, false));

        ProbeNode root = NormalizedGraphMaterializationProbe.Materialize(baseline);

        Assert.Equal(Id(1), root.Id);
        Assert.Equal(10, root.Value);
        Assert.Same(root, root.Next);
        Assert.Null(root.Alias);
    }

    [Fact]
    public void RepeatedSuccessfulMaterializationCreatesFreshGraphsWithInternalSharing() {
        NormalizedBaselineGraph baseline = Baseline(
            rootId: 1,
            (1, 10, 2, 2, false),
            (2, 20, 1, null, false));

        ProbeNode first = NormalizedGraphMaterializationProbe.Materialize(baseline);
        ProbeNode second = NormalizedGraphMaterializationProbe.Materialize(baseline);

        Assert.NotSame(first, second);
        Assert.NotNull(first.Next);
        Assert.NotNull(second.Next);
        Assert.NotSame(first.Next, second.Next);
        Assert.Same(first.Next, first.Alias);
        Assert.Same(second.Next, second.Alias);
        Assert.Same(first, first.Next.Next);
        Assert.Same(second, second.Next.Next);
        Assert.Equal(10, first.Value);
        Assert.Equal(20, first.Next.Value);
        Assert.Equal(10, second.Value);
        Assert.Equal(20, second.Next.Value);
    }

    [Fact]
    public void ConstructorIsBypassedTransientFieldsStayDefaultAndHydrationIsOneTime() {
        ProbeNode normallyConstructed = new(Id(8), value: 80) {
            TransientValue = 81,
            CaptureHook = _ => { },
        };
        NormalizedBaselineGraph baseline = Baseline(
            rootId: 1,
            (1, 10, null, null, true));

        ProbeNode materialized = NormalizedGraphMaterializationProbe.Materialize(baseline);

        Assert.True(normallyConstructed.ConstructorWasRun);
        Assert.False(materialized.ConstructorWasRun);
        Assert.Equal(Id(1), materialized.Id);
        Assert.Equal(10, materialized.Value);
        Assert.Null(materialized.Next);
        Assert.Null(materialized.Alias);
        Assert.Equal(0, materialized.TransientValue);
        Assert.Null(materialized.CaptureHook);
        Assert.True(baseline.Entries[Id(1)].RequiresRewrite);

        ProbeSnapshot attemptedSnapshot = Snapshot(999, nextId: 1, aliasId: 1);
        Assert.Throws<InvalidOperationException>(AttemptRebind);

        Assert.Equal(Id(1), materialized.Id);
        Assert.Equal(10, materialized.Value);
        Assert.Null(materialized.Next);
        Assert.Null(materialized.Alias);
        Assert.Equal(0, materialized.TransientValue);

        void AttemptRebind() {
            materialized.HydrateForMaterialization(
                Id(9),
                in attemptedSnapshot,
                normallyConstructed,
                normallyConstructed);
        }
    }

    [Fact]
    public void LateAllocationFailureReturnsNoRootAndRetryRestartsBothPhases() {
        NormalizedBaselineGraph baseline = LinearThreeNodeBaseline();
        List<PhaseEvent> events = [];
        TestMaterializationHookException inner = new();
        int allocationCalls = 0;
        ProbeNode? result = null;

        ProbeGraphMaterializationException failure =
            Assert.Throws<ProbeGraphMaterializationException>(() =>
                result = NormalizedGraphMaterializationProbe.Materialize(
                    baseline,
                    (phase, id) => events.Add(new PhaseEvent(phase, id)),
                    () => {
                        allocationCalls++;
                        if (allocationCalls == 3) {
                            throw inner;
                        }

                        return (ProbeNode)RuntimeHelpers.GetUninitializedObject(
                            typeof(ProbeNode));
                    }));

        Assert.Null(result);
        Assert.Equal(ProbeMaterializationPhase.Allocation, failure.Phase);
        Assert.Same(inner, failure.InnerException);
        Assert.Equal(3, allocationCalls);
        Assert.Equal(2, events.Count);
        Assert.All(
            events,
            item => Assert.Equal(ProbeMaterializationPhase.Allocation, item.Phase));
        Assert.DoesNotContain(events, item => item.Id == failure.Id);
        AssertIdSet(events.Select(item => item.Id).Append(failure.Id), 1, 2, 3);
        AssertLinearBaselineUnchanged(baseline);

        allocationCalls = 0;
        events.Clear();
        result = NormalizedGraphMaterializationProbe.Materialize(
            baseline,
            (phase, id) => {
                events.Add(new PhaseEvent(phase, id));
                if (phase is ProbeMaterializationPhase.Allocation) {
                    allocationCalls++;
                }
            });

        AssertTwoPass(events, 1, 2, 3);
        Assert.Equal(3, allocationCalls);
        AssertLinearGraph(result);
        AssertLinearBaselineUnchanged(baseline);
    }

    [Fact]
    public void LateHydrationFailureReturnsNoRootAndRetryRestartsBothPhases() {
        NormalizedBaselineGraph baseline = LinearThreeNodeBaseline();
        List<PhaseEvent> events = [];
        TestMaterializationHookException inner = new();
        int hydrationCalls = 0;
        bool fail = true;
        ProbeNode? result = null;

        ProbeGraphMaterializationException failure =
            Assert.Throws<ProbeGraphMaterializationException>(() =>
                result = NormalizedGraphMaterializationProbe.Materialize(
                    baseline,
                    (phase, id) => {
                        events.Add(new PhaseEvent(phase, id));
                        if (phase is ProbeMaterializationPhase.Hydration
                            && ++hydrationCalls == 2
                            && fail) {
                            throw inner;
                        }
                    }));

        Assert.Null(result);
        Assert.Equal(ProbeMaterializationPhase.Hydration, failure.Phase);
        Assert.Equal(events[^1].Id, failure.Id);
        Assert.Same(inner, failure.InnerException);
        Assert.Equal(3, events.Count(item =>
            item.Phase is ProbeMaterializationPhase.Allocation));
        Assert.Equal(2, events.Count(item =>
            item.Phase is ProbeMaterializationPhase.Hydration));
        Assert.All(
            events.Take(3),
            item => Assert.Equal(ProbeMaterializationPhase.Allocation, item.Phase));
        AssertLinearBaselineUnchanged(baseline);

        fail = false;
        hydrationCalls = 0;
        events.Clear();
        result = NormalizedGraphMaterializationProbe.Materialize(
            baseline,
            (phase, id) => {
                events.Add(new PhaseEvent(phase, id));
                if (phase is ProbeMaterializationPhase.Hydration) {
                    hydrationCalls++;
                }
            });

        AssertTwoPass(events, 1, 2, 3);
        Assert.Equal(3, hydrationCalls);
        AssertLinearGraph(result);
        AssertLinearBaselineUnchanged(baseline);
    }

    [Fact]
    public void InvalidBaselineIsRejectedBeforeMaterializationPhasesCanRun() {
        int phaseCalls = 0;
        Action<ProbeMaterializationPhase, ProbeId> hook = (_, _) => phaseCalls++;

        Assert.Throws<InvalidProbeGraphException>(() =>
            _ = NormalizedGraphMaterializationProbe.Materialize(
                new NormalizedBaselineGraph(
                    Id(1),
                    new Dictionary<ProbeId, BaselineEntry> {
                        [Id(1)] = new BaselineEntry(
                            Snapshot(10, nextId: 99),
                            RequiresRewrite: false),
                    }),
                hook));
        Assert.Throws<InvalidProbeGraphException>(() =>
            _ = NormalizedGraphMaterializationProbe.Materialize(
                new NormalizedBaselineGraph(
                    default,
                    new Dictionary<ProbeId, BaselineEntry> {
                        [Id(1)] = new BaselineEntry(
                            Snapshot(10),
                            RequiresRewrite: false),
                    }),
                hook));

        Assert.Equal(0, phaseCalls);
    }

    private static NormalizedBaselineGraph LinearThreeNodeBaseline() {
        return Baseline(
            rootId: 1,
            (1, 10, 2, null, true),
            (2, 20, 3, null, false),
            (3, 30, null, null, false));
    }

    private static void AssertLinearGraph(ProbeNode root) {
        Assert.Equal(Id(1), root.Id);
        Assert.Equal(10, root.Value);
        Assert.Null(root.Alias);
        Assert.NotNull(root.Next);
        Assert.Equal(Id(2), root.Next.Id);
        Assert.Equal(20, root.Next.Value);
        Assert.Null(root.Next.Alias);
        Assert.NotNull(root.Next.Next);
        Assert.Equal(Id(3), root.Next.Next.Id);
        Assert.Equal(30, root.Next.Next.Value);
        Assert.Null(root.Next.Next.Next);
        Assert.Null(root.Next.Next.Alias);
    }

    private static void AssertLinearBaselineUnchanged(
        NormalizedBaselineGraph baseline) {
        Assert.Equal(Id(1), baseline.RootId);
        Assert.Equal(3, baseline.Entries.Count);
        Assert.Equal(
            new BaselineEntry(Snapshot(10, nextId: 2), true),
            baseline.Entries[Id(1)]);
        Assert.Equal(
            new BaselineEntry(Snapshot(20, nextId: 3), false),
            baseline.Entries[Id(2)]);
        Assert.Equal(
            new BaselineEntry(Snapshot(30), false),
            baseline.Entries[Id(3)]);
    }

    private static void AssertTwoPass(
        IReadOnlyList<PhaseEvent> events,
        params long[] expectedIds) {
        Assert.Equal(expectedIds.Length * 2, events.Count);
        int firstHydrationIndex = -1;
        for (int index = 0; index < events.Count; index++) {
            if (events[index].Phase is ProbeMaterializationPhase.Hydration) {
                firstHydrationIndex = index;
                break;
            }
        }

        Assert.Equal(expectedIds.Length, firstHydrationIndex);
        Assert.All(
            events.Take(firstHydrationIndex),
            item => Assert.Equal(ProbeMaterializationPhase.Allocation, item.Phase));
        Assert.All(
            events.Skip(firstHydrationIndex),
            item => Assert.Equal(ProbeMaterializationPhase.Hydration, item.Phase));
        AssertIdSet(
            events.Take(firstHydrationIndex).Select(item => item.Id),
            expectedIds);
        AssertIdSet(
            events.Skip(firstHydrationIndex).Select(item => item.Id),
            expectedIds);
    }

    private static void AssertIdSet(
        IEnumerable<ProbeId> actual,
        params long[] expectedIds) {
        HashSet<ProbeId> actualSet = actual.ToHashSet();
        HashSet<ProbeId> expectedSet = expectedIds.Select(Id).ToHashSet();
        Assert.True(
            actualSet.SetEquals(expectedSet),
            $"Expected IDs {{{string.Join(", ", expectedSet)}}}, " +
            $"actual {{{string.Join(", ", actualSet)}}}.");
    }

    private static NormalizedBaselineGraph Baseline(
        long rootId,
        params BaselineSpec[] entries) {
        Dictionary<ProbeId, BaselineEntry> table = entries.ToDictionary(
            entry => Id(entry.Id),
            entry => new BaselineEntry(
                Snapshot(
                    entry.Value,
                    entry.NextId,
                    entry.AliasId),
                entry.RequiresRewrite));
        return new NormalizedBaselineGraph(Id(rootId), table);
    }

    private static ProbeId Id(long value) {
        return new ProbeId(value);
    }

    private static ProbeSnapshot Snapshot(
        int value,
        long? nextId = null,
        long? aliasId = null) {
        return new ProbeSnapshot(
            value,
            nextId is long next ? Id(next) : null,
            aliasId is long alias ? Id(alias) : null);
    }

    private readonly record struct PhaseEvent(
        ProbeMaterializationPhase Phase,
        ProbeId Id);

    private readonly record struct BaselineSpec(
        long Id,
        int Value,
        long? NextId,
        long? AliasId,
        bool RequiresRewrite) {
        public static implicit operator BaselineSpec(
            (long Id, int Value, long? NextId, long? AliasId, bool RequiresRewrite) value) {
            return new BaselineSpec(
                value.Id,
                value.Value,
                value.NextId,
                value.AliasId,
                value.RequiresRewrite);
        }
    }

    private sealed class TestMaterializationHookException : Exception { }
}
