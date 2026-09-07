using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.Tests;

public sealed class RuntimeReferenceGraphTests {
    [Fact]
    public void NominalValidationUsesEachDtosSchemaAncestryAndChecksEveryReferenceKind() {
        DurableSchema oldBase = new("old-base", 1);
        DurableSchema newBase = new("new-base", 1);
        DurableSchema oldDerived = new("derived", 1, [], oldBase);
        DurableSchema newDerived = new("derived", 2, [], newBase);
        StateReferenceValidator stored = new(new Dictionary<uint, ObjectStateRecord> {
            [1] = new(1, oldDerived, 0), [2] = new(2, "text"),
        });
        StateReferenceValidator current = new(new Dictionary<uint, ObjectStateRecord> {
            [1] = new(1, newDerived, 0), [2] = new(2, "text"),
        });
        stored.VisitDurable(1, "old-base");
        stored.VisitDurable(1, "derived");
        current.VisitDurable(1, "new-base");
        Assert.Throws<InvalidDataException>(() => current.VisitDurable(1, "old-base"));
        Assert.Throws<InvalidDataException>(() => stored.VisitDurable(1, "new-base"));
        Assert.Throws<InvalidDataException>(() => stored.VisitDurable(1, "Old-base"));
        Assert.Throws<InvalidDataException>(() => stored.VisitDurable(2, "derived"));
        Assert.Throws<InvalidDataException>(() => stored.VisitDurable(3, "derived"));
        Assert.Throws<InvalidDataException>(() => stored.VisitString(1));
        Assert.Throws<InvalidDataException>(() => stored.VisitString(3));
        stored.VisitString(2);
        stored.VisitString(0);
        stored.VisitDurable(0, "unregistered-family");
        Assert.Throws<ArgumentException>(() => stored.VisitDurable(0, " "));
    }

    [Fact]
    public void ObjectReadTableCopiesDirectoryAndResolvesSharedTypedInstances() {
        Node first = new(), second = new();
        Dictionary<uint, DurableBase> source = new() { [1] = first, [2] = second };
        string text = new(['x']);
        ObjectReadTable table = new(StringReadTable.FromDecoded([(3, text)]), source);
        source.Clear();
        Assert.Same(first, table.ResolveDurable<Node>(1));
        Assert.Same(first, table.ResolveDurable<DurableBase>(1));
        Assert.NotSame(table.ResolveDurable<Node>(1), table.ResolveDurable<Node>(2));
        Assert.Same(text, table.ResolveString(3));
        Assert.Null(table.ResolveDurable<Node>(0));
        Assert.Null(table.ResolveString(0));
        Assert.Throws<InvalidDataException>(() => table.ResolveDurable<UnknownNode>(1));
        Assert.Throws<InvalidDataException>(() => table.ResolveDurable<Node>(3));
        Assert.Throws<InvalidDataException>(() => table.ResolveString(1));
    }

    [Fact]
    public void ObjectReadTableRejectsAliasesZeroAndNullAllocations() {
        Node instance = new();
        StringReadTable strings = StringReadTable.FromDecoded([]);
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(strings,
            new Dictionary<uint, DurableBase> { [1] = instance, [2] = instance }));
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(strings,
            new Dictionary<uint, DurableBase> { [0] = instance }));
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(strings,
            new Dictionary<uint, DurableBase> { [1] = null! }));
    }

    [Fact]
    public void CaptureQueueInternsCyclesAndAliasesOnceAndKeepsChildrenOutOfRoots() {
        int calls = 0;
        StateModelBinding model = Model(onCapture: () => calls++);
        Node first = new() { Value = 10 }, second = new() { Value = 20 };
        first.Next = second;
        first.Alias = second;
        second.Next = first;
        second.Alias = second;
        CaptureSession session = new();
        List<StateModelBinding> models = [model];
        using CaptureContext context = session.BeginCapture(models);
        // Mutating the caller's catalog after BeginCapture cannot remove the selected binding.
        models.Clear();
        uint root = model.AddRoot(context, first);
        CapturedGraph graph = context.Seal();
        Assert.Equal(root, Assert.Single(graph.RootIds));
        Assert.Equal(2, calls);
        Assert.Equal(2, graph.Objects.Count);
        NodeState firstState = graph.Objects.Single(item => item.Id == root).GetState<NodeState>();
        Assert.Equal(firstState.Next, firstState.Alias);
        NodeState secondState = graph.Objects.Single(item => item.Id == firstState.Next).GetState<NodeState>();
        Assert.Equal(root, secondState.Next);
        Assert.Equal(firstState.Next, secondState.Alias);
        second.Value = 99;
        Assert.Equal((byte)20, graph.Objects.Single(item => item.Id == firstState.Next).GetState<NodeState>().Value);
    }

    [Fact]
    public void CaptureQueueHandlesADeepChainWithoutDomainDepthRecursion() {
        const int count = 12000;
        Node root = new(), tail = root;
        for (int index = 1; index < count; index++) {
            tail.Next = new Node();
            tail = (Node)tail.Next;
        }
        StateModelBinding model = Model();
        CaptureSession session = new();
        using CaptureContext context = session.BeginCapture([model]);
        model.AddRoot(context, root);
        CapturedGraph graph = context.Seal();
        Assert.Equal(count, graph.Objects.Count);
        Assert.Single(graph.RootIds);
        Assert.Equal(0u, graph.Objects[^1].GetState<NodeState>().Next);
    }

    [Fact]
    public void AlreadyInternedAliasStillValidatesItsOwnNominalConstraintAndFailureReleasesCapture() {
        Node child = new(), root = new() { Next = child, Alias = child };
        StateModelBinding invalid = Model(aliasNominal: "different-family");
        CaptureSession session = new();
        using (CaptureContext context = session.BeginCapture([invalid])) {
            invalid.AddRoot(context, root);
            Assert.Throws<InvalidOperationException>(() => context.Seal());
        }
        Assert.Null(session.Current);
        StateModelBinding valid = Model();
        using CaptureContext retry = session.BeginCapture([valid]);
        valid.AddRoot(retry, root);
        Assert.Equal(2, retry.Seal().Objects.Count);
    }

    [Fact]
    public void UnknownActualSubtypeNeverFallsBackToRegisteredBaseAndNullNeedsNoTargetModel() {
        StateModelBinding model = Model();
        CaptureSession session = new();
        using (CaptureContext context = session.BeginCapture([model])) {
            model.AddRoot(context, new Node { Next = new UnknownNode() });
            Assert.Throws<InvalidOperationException>(() => context.Seal());
        }
        StateModelBinding nullTarget = Model(aliasNominal: "unregistered-family");
        using CaptureContext retry = session.BeginCapture([nullTarget]);
        nullTarget.AddRoot(retry, new Node());
        Assert.Single(retry.Seal().Objects);
    }

    [Fact]
    public void ConflictingCatalogEnumerationFailsBeforeCaptureAndDoesNotOccupyTheSession() {
        StateModelBinding first = Model();
        StateModelBinding sameTypeDifferentFamily = Model(schemaId: "other-node");
        CaptureSession session = new();
        Assert.Throws<ArgumentException>(() => session.BeginCapture([first, sameTypeDifferentFamily]));
        using CaptureContext context = session.BeginCapture([first, first]);
        first.AddRoot(context, new Node());
        Assert.Single(context.Seal().Objects);
    }

    private class Node : DurableBase {
        internal byte Value;
        internal DurableBase? Next;
        internal DurableBase? Alias;
    }
    private sealed class UnknownNode : Node { }
    private readonly record struct NodeState(byte Value, uint Next, uint Alias);

    private static StateModelBinding Model(string schemaId = "node", string? aliasNominal = null, Action? onCapture = null) {
        DurableSchema schema = new(schemaId, 1,
            new DurableFieldInfo(1, TypeTag.Byte),
            new DurableFieldInfo(2, TypeTag.DurableReference, schemaId),
            new DurableFieldInfo(3, TypeTag.DurableReference, aliasNominal ?? schemaId));
        CapturedStatePreparation<NodeState> preparation = new(schema,
            static (in NodeState state) => throw new NotSupportedException("Capture tests do not prepare payloads."),
            static (in NodeState prior, in NodeState next) => throw new NotSupportedException("Capture tests do not prepare payloads."));
        void Visit(in NodeState state, IStateReferenceVisitor visitor) {
            visitor.VisitDurable(state.Next, schemaId);
            visitor.VisitDurable(state.Alias, aliasNominal ?? schemaId);
        }
        StateReaderBinding<NodeState> reader = new(schema,
            static (ref BinaryPayloadReader body) => throw new NotSupportedException(),
            static (ref BinaryPayloadReader body, in NodeState prior) => throw new NotSupportedException(), Visit);
        return new StateModelBinding<Node, NodeState>(preparation, [reader],
            static row => row.GetState<NodeState>(), static () => new Node(),
            static (Node node, in NodeState state, ObjectReadTable objects) => {
                node.Value = state.Value;
                node.Next = objects.ResolveDurable<Node>(state.Next);
                node.Alias = objects.ResolveDurable<Node>(state.Alias);
            },
            (node, context) => {
                onCapture?.Invoke();
                return new(node.Value, context.CaptureDurable(node.Next, schemaId),
                    context.CaptureDurable(node.Alias, aliasNominal ?? schemaId));
            }, Visit);
    }
}
