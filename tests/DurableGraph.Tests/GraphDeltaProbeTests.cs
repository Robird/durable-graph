namespace Atelia.DurableGraph.Tests;

public sealed class GraphDeltaProbeTests {
    [Fact]
    public void UnchangedGraphProducesEmptyDelta() {
        ProbeNode child = Node(2, 20);
        ProbeNode root = Node(1, 10, next: child, alias: child);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2, aliasId: 2)),
            (2, Snapshot(20)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Equal(Id(1), delta.ResultRootId);
        Assert.Empty(delta.Upserts);
        Assert.Empty(delta.UnreachableIds);
        AssertGraphEqual(baseline, GraphDeltaProbe.AcceptForAssertion(baseline, delta));
    }

    [Fact]
    public void TransientOnlyMutationProducesEmptyDelta() {
        ProbeNode root = Node(1, 10);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10)));
        root.TransientValue = 1234;

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Empty(delta.Upserts);
        Assert.Empty(delta.UnreachableIds);
        AssertSnapshot(Snapshot(10), baseline.Entries[Id(1)].Snapshot);
    }

    [Fact]
    public void LeafScalarChangeUpsertsOnlyLeaf() {
        ProbeNode child = Node(2, 21);
        ProbeNode root = Node(1, 10, next: child);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        AssertUpserts(delta, (2, Snapshot(21)));
        Assert.Empty(delta.UnreachableIds);
        Assert.False(delta.Upserts.ContainsKey(Id(1)));
        AssertSnapshot(Snapshot(10, nextId: 2), baseline.Entries[Id(1)].Snapshot);
    }

    [Fact]
    public void SameValuedReplacementWithNewIdChangesParentAndReachability() {
        ProbeNode replacement = Node(3, 20);
        ProbeNode root = Node(1, 10, next: replacement);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        AssertUpserts(
            delta,
            (1, Snapshot(10, nextId: 3)),
            (3, Snapshot(20)));
        AssertIds(delta.UnreachableIds, 2);
    }

    [Fact]
    public void SharedChildIsCapturedOnlyOnce() {
        int childCaptures = 0;
        ProbeNode child = Node(2, 20);
        child.CaptureHook = _ => childCaptures++;
        ProbeNode root = Node(1, 10, next: child, alias: child);
        NormalizedBaselineGraph baseline = Graph(rootId: 1, (1, Snapshot(10)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Equal(1, childCaptures);
        AssertUpserts(
            delta,
            (1, Snapshot(10, nextId: 2, aliasId: 2)),
            (2, Snapshot(20)));
        Assert.Empty(delta.UnreachableIds);
    }

    [Fact]
    public void SelfCycleTerminatesAndCapturesNodeOnce() {
        int captures = 0;
        ProbeNode root = Node(1, 10);
        root.Next = root;
        root.CaptureHook = _ => captures++;
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 1)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Equal(1, captures);
        Assert.Empty(delta.Upserts);
        Assert.Empty(delta.UnreachableIds);
    }

    [Fact]
    public void TwoNodeCycleTerminatesAndCapturesEachNodeOnce() {
        int firstCaptures = 0;
        int secondCaptures = 0;
        ProbeNode first = Node(1, 10);
        ProbeNode second = Node(2, 20);
        first.Next = second;
        second.Next = first;
        first.CaptureHook = _ => firstCaptures++;
        second.CaptureHook = _ => secondCaptures++;
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(11, nextId: 2)),
            (2, Snapshot(21, nextId: 1)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, first);

        Assert.Equal(1, firstCaptures);
        Assert.Equal(1, secondCaptures);
        AssertUpserts(
            delta,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20, nextId: 1)));
        Assert.Empty(delta.UnreachableIds);
    }

    [Fact]
    public void DifferentInstancesWithSameIdFailClosedWithoutChangingBaseline() {
        int conflictingNodeCaptures = 0;
        ProbeNode first = Node(2, 20);
        ProbeNode impostor = Node(2, 20);
        first.CaptureHook = _ => conflictingNodeCaptures++;
        impostor.CaptureHook = _ => conflictingNodeCaptures++;
        ProbeNode root = Node(1, 10, next: first, alias: impostor);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10)),
            (9, Snapshot(90), true));
        NormalizedBaselineGraph expected = Graph(
            rootId: 1,
            (1, Snapshot(10)),
            (9, Snapshot(90), true));

        Assert.Throws<ProbeIdentityConflictException>(
            () => GraphDeltaProbe.PlanSave(baseline, root));

        Assert.Equal(1, conflictingNodeCaptures);
        AssertGraphEqual(expected, baseline);
    }

    [Fact]
    public void RemovingOneSharedEdgeKeepsChildReachable() {
        ProbeNode child = Node(2, 20);
        ProbeNode root = Node(1, 10, alias: child);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2, aliasId: 2)),
            (2, Snapshot(20)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        AssertUpserts(delta, (1, Snapshot(10, aliasId: 2)));
        Assert.Empty(delta.UnreachableIds);
        Assert.False(delta.Upserts.ContainsKey(Id(2)));
    }

    [Fact]
    public void RemovingLastEdgeToCycleMakesEntireCycleUnreachable() {
        ProbeNode root = Node(1, 10);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20, nextId: 3)),
            (3, Snapshot(30, nextId: 2)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        AssertUpserts(delta, (1, Snapshot(10)));
        AssertIds(delta.UnreachableIds, 2, 3);
    }

    [Fact]
    public void RootReplacementCanChangeOnlyResultRootId() {
        ProbeNode first = Node(1, 10);
        ProbeNode second = Node(2, 20);
        first.Next = second;
        second.Next = first;
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20, nextId: 1)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, second);

        Assert.Equal(Id(2), delta.ResultRootId);
        Assert.Empty(delta.Upserts);
        Assert.Empty(delta.UnreachableIds);

        NormalizedBaselineGraph accepted = GraphDeltaProbe.AcceptForAssertion(baseline, delta);
        NormalizedBaselineGraph captured = GraphDeltaProbe.CaptureCleanForAssertion(second);
        Assert.Equal(Id(2), accepted.RootId);
        AssertGraphEqual(captured, accepted);
    }

    [Fact]
    public void ReachableRewriteRequiredEntryIsFullyUpsertedEvenWhenEqual() {
        ProbeNode root = Node(1, 10);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10), true));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        AssertUpserts(delta, (1, Snapshot(10)));
        Assert.Empty(delta.UnreachableIds);
        Assert.True(baseline.Entries[Id(1)].RequiresRewrite);
    }

    [Fact]
    public void UnreachableRewriteRequiredEntryIsOnlyUnreachable() {
        ProbeNode root = Node(1, 10);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10)),
            (2, Snapshot(20), true));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Empty(delta.Upserts);
        AssertIds(delta.UnreachableIds, 2);
        Assert.True(baseline.Entries[Id(2)].RequiresRewrite);
    }

    [Fact]
    public void InvalidBaselineIdsAndReferencesAreRejected() {
        Assert.Throws<InvalidProbeGraphException>(
            () => Graph(
                rootId: 1,
                (1, Snapshot(10, nextId: 99))));

        Dictionary<ProbeId, BaselineEntry> defaultIdEntry = new() {
            [default] = new BaselineEntry(Snapshot(10), RequiresRewrite: false),
        };
        Assert.Throws<InvalidProbeGraphException>(
            () => new NormalizedBaselineGraph(default, defaultIdEntry));
    }

    [Fact]
    public void CaptureFailurePreservesBaselineAndRewriteObligationForRetry() {
        int rootCaptures = 0;
        int childCaptures = 0;
        ProbeNode child = Node(2, 20);
        ProbeNode root = Node(1, 11, next: child);
        root.CaptureHook = _ => rootCaptures++;
        child.CaptureHook = _ => {
            childCaptures++;
            throw new TestCaptureException();
        };
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20), true));
        NormalizedBaselineGraph expected = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2)),
            (2, Snapshot(20), true));

        Assert.Throws<TestCaptureException>(() => GraphDeltaProbe.PlanSave(baseline, root));
        Assert.Equal(1, rootCaptures);
        Assert.Equal(1, childCaptures);
        AssertGraphEqual(expected, baseline);

        root.CaptureHook = null;
        child.CaptureHook = null;
        GraphDelta firstRetry = GraphDeltaProbe.PlanSave(baseline, root);
        GraphDelta secondRetry = GraphDeltaProbe.PlanSave(baseline, root);

        AssertDeltaEqual(firstRetry, secondRetry);
        AssertUpserts(
            firstRetry,
            (1, Snapshot(11, nextId: 2)),
            (2, Snapshot(20)));
        Assert.False(baseline.Entries[Id(1)].RequiresRewrite);
        Assert.True(baseline.Entries[Id(2)].RequiresRewrite);
    }

    [Fact]
    public void AcceptedDeltaMakesSecondPlanEmpty() {
        ProbeNode root = Node(1, 11);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10), true));

        GraphDelta first = GraphDeltaProbe.PlanSave(baseline, root);
        NormalizedBaselineGraph accepted = GraphDeltaProbe.AcceptForAssertion(baseline, first);
        GraphDelta second = GraphDeltaProbe.PlanSave(accepted, root);

        AssertUpserts(first, (1, Snapshot(11)));
        Assert.False(accepted.Entries[Id(1)].RequiresRewrite);
        Assert.Empty(second.Upserts);
        Assert.Empty(second.UnreachableIds);
    }

    [Fact]
    public void AcceptRejectsMalformedCandidatesWithoutChangingBaseline() {
        NormalizedBaselineGraph cleanBaseline = Graph(
            rootId: 1,
            (1, Snapshot(10)));
        NormalizedBaselineGraph expectedClean = Graph(
            rootId: 1,
            (1, Snapshot(10)));

        GraphDelta overlapping = new(
            resultRootId: Id(1),
            new Dictionary<ProbeId, ProbeSnapshot> {
                [Id(1)] = Snapshot(11),
            },
            new[] { Id(1) });
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(cleanBaseline, overlapping));
        AssertGraphEqual(expectedClean, cleanBaseline);

        GraphDelta missingResultRoot = new(
            resultRootId: Id(2),
            new Dictionary<ProbeId, ProbeSnapshot>(),
            Array.Empty<ProbeId>());
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(cleanBaseline, missingResultRoot));
        AssertGraphEqual(expectedClean, cleanBaseline);

        GraphDelta danglingFinalReference = new(
            resultRootId: Id(1),
            new Dictionary<ProbeId, ProbeSnapshot> {
                [Id(1)] = Snapshot(11, nextId: 99),
            },
            Array.Empty<ProbeId>());
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(cleanBaseline, danglingFinalReference));
        AssertGraphEqual(expectedClean, cleanBaseline);

        GraphDelta disconnectedNewUpsert = new(
            resultRootId: Id(1),
            new Dictionary<ProbeId, ProbeSnapshot> {
                [Id(2)] = Snapshot(20),
            },
            Array.Empty<ProbeId>());
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(cleanBaseline, disconnectedNewUpsert));
        AssertGraphEqual(expectedClean, cleanBaseline);

        NormalizedBaselineGraph disconnectedCleanBaseline = Graph(
            rootId: 1,
            (1, Snapshot(10)),
            (2, Snapshot(20)));
        NormalizedBaselineGraph expectedDisconnectedClean = Graph(
            rootId: 1,
            (1, Snapshot(10)),
            (2, Snapshot(20)));
        GraphDelta omittedDisconnectedOldEntry = new(
            resultRootId: Id(1),
            new Dictionary<ProbeId, ProbeSnapshot>(),
            Array.Empty<ProbeId>());
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(
                disconnectedCleanBaseline,
                omittedDisconnectedOldEntry));
        AssertGraphEqual(expectedDisconnectedClean, disconnectedCleanBaseline);

        NormalizedBaselineGraph flaggedBaseline = Graph(
            rootId: 1,
            (1, Snapshot(10), true));
        NormalizedBaselineGraph expectedFlagged = Graph(
            rootId: 1,
            (1, Snapshot(10), true));
        GraphDelta omittedRewriteObligation = new(
            resultRootId: Id(1),
            new Dictionary<ProbeId, ProbeSnapshot>(),
            Array.Empty<ProbeId>());
        Assert.Throws<InvalidGraphDeltaException>(
            () => GraphDeltaProbe.AcceptForAssertion(flaggedBaseline, omittedRewriteObligation));
        AssertGraphEqual(expectedFlagged, flaggedBaseline);
    }

    [Fact]
    public void GraphDeltaRejectsDefaultIdsAtEveryAggregateBoundary() {
        Assert.Throws<InvalidGraphDeltaException>(
            () => new GraphDelta(
                default,
                new Dictionary<ProbeId, ProbeSnapshot>(),
                Array.Empty<ProbeId>()));

        Dictionary<ProbeId, ProbeSnapshot> defaultUpsertKey = new() {
            [default] = Snapshot(10),
        };
        Assert.Throws<InvalidGraphDeltaException>(
            () => new GraphDelta(Id(1), defaultUpsertKey, Array.Empty<ProbeId>()));

        Dictionary<ProbeId, ProbeSnapshot> defaultReference = new() {
            [Id(1)] = new ProbeSnapshot(
                Value: 10,
                NextId: (ProbeId?)default(ProbeId),
                AliasId: null),
        };
        Assert.Throws<InvalidGraphDeltaException>(
            () => new GraphDelta(Id(1), defaultReference, Array.Empty<ProbeId>()));

        Assert.Throws<InvalidGraphDeltaException>(
            () => new GraphDelta(
                Id(1),
                new Dictionary<ProbeId, ProbeSnapshot>(),
                new[] { default(ProbeId) }));
    }

    [Fact]
    public void AggregatesDefensivelyCopyInputsAndSupportExplicitOrderedProjection() {
        ProbeNode first = Node(1, 10);
        ProbeNode second = Node(2, 20);
        ProbeNode root = Node(3, 30, next: first, alias: second);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 30,
            (30, Snapshot(300)),
            (10, Snapshot(100)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);

        Assert.Equal(
            new[] { Id(1), Id(2), Id(3) },
            OrderedIds(delta.Upserts.Keys));
        Assert.Equal(
            new[] { Id(10), Id(30) },
            OrderedIds(delta.UnreachableIds));
        AssertUpserts(
            delta,
            (1, Snapshot(10)),
            (2, Snapshot(20)),
            (3, Snapshot(30, nextId: 1, aliasId: 2)));

        Dictionary<ProbeId, ProbeSnapshot> inputUpserts = new() {
            [Id(long.MaxValue)] = Snapshot(900),
            [Id(1)] = Snapshot(100),
        };
        HashSet<ProbeId> inputUnreachable = [Id(long.MaxValue - 1), Id(2)];
        GraphDelta copiedDelta = new(Id(1), inputUpserts, inputUnreachable);
        inputUpserts.Clear();
        inputUnreachable.Clear();

        Assert.Equal(
            new[] { Id(1), Id(long.MaxValue) },
            OrderedIds(copiedDelta.Upserts.Keys));
        AssertSnapshot(Snapshot(100), copiedDelta.Upserts[Id(1)]);
        AssertSnapshot(Snapshot(900), copiedDelta.Upserts[Id(long.MaxValue)]);
        Assert.Equal(
            new[] { Id(2), Id(long.MaxValue - 1) },
            OrderedIds(copiedDelta.UnreachableIds));
    }

    [Fact]
    public void CapturedBaselineIsDetachedFromLaterLiveMutation() {
        Dictionary<ProbeId, BaselineEntry> inputEntries = new() {
            [Id(2)] = new BaselineEntry(Snapshot(20), RequiresRewrite: false),
            [Id(1)] = new BaselineEntry(
                Snapshot(10, nextId: 2),
                RequiresRewrite: false),
        };
        NormalizedBaselineGraph copiedBaseline = new(Id(1), inputEntries);
        inputEntries.Clear();

        Assert.Equal(
            new[] { Id(1), Id(2) },
            OrderedIds(copiedBaseline.Entries.Keys));
        AssertSnapshot(
            Snapshot(10, nextId: 2),
            copiedBaseline.Entries[Id(1)].Snapshot);
        AssertSnapshot(Snapshot(20), copiedBaseline.Entries[Id(2)].Snapshot);

        ProbeNode child = Node(2, 20);
        ProbeNode root = Node(1, 10, next: child);

        NormalizedBaselineGraph baseline = GraphDeltaProbe.CaptureCleanForAssertion(root);
        root.Value = 11;
        root.Next = null;
        root.Alias = child;
        child.Value = 21;
        root.TransientValue = 100;

        Assert.Equal(Id(1), baseline.RootId);
        AssertSnapshot(Snapshot(10, nextId: 2), baseline.Entries[Id(1)].Snapshot);
        AssertSnapshot(Snapshot(20), baseline.Entries[Id(2)].Snapshot);
        Assert.All(baseline.Entries.Values, entry => Assert.False(entry.RequiresRewrite));
    }

    [Fact]
    public void AcceptOfPlanEqualsCleanCaptureForRepresentativeGraph() {
        ProbeNode retained = Node(2, 21);
        ProbeNode added = Node(3, 30);
        added.Next = added;
        added.Alias = retained;
        ProbeNode root = Node(1, 10, next: added, alias: retained);
        NormalizedBaselineGraph baseline = Graph(
            rootId: 1,
            (1, Snapshot(10, nextId: 2, aliasId: 2)),
            (2, Snapshot(21), true),
            (7, Snapshot(70, nextId: 8)),
            (8, Snapshot(80, nextId: 7)));

        GraphDelta delta = GraphDeltaProbe.PlanSave(baseline, root);
        NormalizedBaselineGraph accepted = GraphDeltaProbe.AcceptForAssertion(baseline, delta);
        NormalizedBaselineGraph captured = GraphDeltaProbe.CaptureCleanForAssertion(root);

        AssertUpserts(
            delta,
            (1, Snapshot(10, nextId: 3, aliasId: 2)),
            (2, Snapshot(21)),
            (3, Snapshot(30, nextId: 3, aliasId: 2)));
        AssertIds(delta.UnreachableIds, 7, 8);
        AssertGraphEqual(captured, accepted);
        Assert.All(accepted.Entries.Values, entry => Assert.False(entry.RequiresRewrite));
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

    private static ProbeNode Node(
        long id,
        int value,
        ProbeNode? next = null,
        ProbeNode? alias = null) {
        return new ProbeNode(Id(id), value) {
            Next = next,
            Alias = alias,
        };
    }

    private static NormalizedBaselineGraph Graph(
        long rootId,
        params BaselineSpec[] entries) {
        Dictionary<ProbeId, BaselineEntry> map = entries.ToDictionary(
            entry => Id(entry.Id),
            entry => new BaselineEntry(entry.Snapshot, entry.RequiresRewrite));
        return new NormalizedBaselineGraph(Id(rootId), map);
    }

    private static void AssertUpserts(
        GraphDelta actual,
        params (long Id, ProbeSnapshot Snapshot)[] expected) {
        Assert.Equal(expected.Length, actual.Upserts.Count);

        foreach ((long id, ProbeSnapshot snapshot) in expected) {
            Assert.True(actual.Upserts.TryGetValue(Id(id), out ProbeSnapshot actualSnapshot));
            AssertSnapshot(snapshot, actualSnapshot);
        }
    }

    private static void AssertIds(
        IEnumerable<ProbeId> actual,
        params long[] expected) {
        Assert.Equal(OrderedIds(expected.Select(Id)), OrderedIds(actual));
    }

    private static void AssertSnapshot(ProbeSnapshot expected, ProbeSnapshot actual) {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.NextId, actual.NextId);
        Assert.Equal(expected.AliasId, actual.AliasId);
    }

    private static void AssertDeltaEqual(GraphDelta expected, GraphDelta actual) {
        Assert.Equal(expected.ResultRootId, actual.ResultRootId);
        Assert.Equal(
            OrderedIds(expected.Upserts.Keys),
            OrderedIds(actual.Upserts.Keys));

        foreach ((ProbeId id, ProbeSnapshot snapshot) in expected.Upserts) {
            Assert.True(actual.Upserts.TryGetValue(id, out ProbeSnapshot actualSnapshot));
            AssertSnapshot(snapshot, actualSnapshot);
        }

        Assert.Equal(
            OrderedIds(expected.UnreachableIds),
            OrderedIds(actual.UnreachableIds));
    }

    private static void AssertGraphEqual(
        NormalizedBaselineGraph expected,
        NormalizedBaselineGraph actual) {
        Assert.Equal(expected.RootId, actual.RootId);
        Assert.Equal(
            OrderedIds(expected.Entries.Keys),
            OrderedIds(actual.Entries.Keys));

        foreach ((ProbeId id, BaselineEntry entry) in expected.Entries) {
            Assert.True(actual.Entries.TryGetValue(id, out BaselineEntry actualEntry));
            AssertSnapshot(entry.Snapshot, actualEntry.Snapshot);
            Assert.Equal(entry.RequiresRewrite, actualEntry.RequiresRewrite);
        }
    }

    private static ProbeId[] OrderedIds(IEnumerable<ProbeId> ids) {
        return ids.OrderBy(id => id.Value).ToArray();
    }

    private readonly record struct BaselineSpec(
        long Id,
        ProbeSnapshot Snapshot,
        bool RequiresRewrite) {
        public static implicit operator BaselineSpec(
            (long Id, ProbeSnapshot Snapshot) value) {
            return new BaselineSpec(value.Id, value.Snapshot, RequiresRewrite: false);
        }

        public static implicit operator BaselineSpec(
            (long Id, ProbeSnapshot Snapshot, bool RequiresRewrite) value) {
            return new BaselineSpec(value.Id, value.Snapshot, value.RequiresRewrite);
        }
    }

    private sealed class TestCaptureException : Exception { }
}
