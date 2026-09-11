namespace Atelia.DurableGraph.Tests;

public sealed class UnifiedObjectPathTests {
    [Fact]
    public void CommonCaptureKeepsStringIdentityAndCanonicalizesEmptyStrings() {
        string first = new(['x']), second = new(['x']);
        CaptureSession session = new();
        using CaptureContext capture = session.BeginCapture();
        capture.AddRoot(new Root(), new DurableSchema("root", 1), (_, context) => {
            ObjectId firstId = context.CaptureObject(first, TypeExpr.Builtin(TypeTag.String));
            Assert.Equal(firstId, context.CaptureString(first));
            ObjectId secondId = context.CaptureString(second);
            Assert.NotEqual(firstId, secondId);
            ObjectId emptyId = context.CaptureObject(new string([]), TypeExpr.Builtin(TypeTag.String));
            Assert.Equal(emptyId, context.CaptureString(string.Empty));
            Assert.Equal(default, context.CaptureObject(null, TypeExpr.Named("absent")));
            return firstId;
        });
        CapturedGraph graph = capture.Seal();
        Assert.Equal(4, graph.Objects.Count);
        Assert.Same(string.Empty, graph.Objects.Single(item => item.Kind == ObjectStateKind.String && item.StringContent.Length == 0).StringContent);
        Assert.Equal(2, graph.Objects.Count(item => item.Kind == ObjectStateKind.String && item.StringContent == "x"));
    }

    [Fact]
    public void CommonCaptureValidatesAliasConstraintBeforeIdentityLookup() {
        CaptureSession session = new();
        using CaptureContext capture = session.BeginCapture();
        capture.AddRoot(new Root(), new DurableSchema("root", 1), (_, context) => {
            context.CaptureString("already-interned");
            return context.CaptureObject("already-interned", TypeExpr.Named("wrong-family"));
        });
        Assert.Throws<InvalidOperationException>(() => capture.Seal());
        Assert.Null(session.Current);
        using CaptureContext retry = session.BeginCapture();
    }

    [Fact]
    public void UnifiedReadTableKeepsArrayAndStringInstancesAndRejectsMergedIdentities() {
        int[] first = new int[0], second = new int[0];
        Dictionary<ObjectId, object> source = new() {
            [new(1)] = first, [new(2)] = second,
            [new(3)] = string.Empty, [new(4)] = new string([]),
        };
        ObjectReadTable table = new(source);
        source.Clear();
        Assert.Same(first, table.ResolveObject<int[]>(new(1)));
        Assert.Same(second, table.ResolveObject<int[]>(new(2)));
        Assert.Same(string.Empty, table.ResolveObject<string>(new(3)));
        Assert.Same(string.Empty, table.ResolveString(new(4)));
        Assert.Null(table.ResolveObject<int[]>(default));
        Assert.Throws<InvalidDataException>(() => table.ResolveObject<string>(new(1)));
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(new Dictionary<ObjectId, object> {
            [new(1)] = first, [new(2)] = first,
        }));
        string text = new(['z']);
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(new Dictionary<ObjectId, object> {
            [new(1)] = text, [new(2)] = text,
        }));
        Assert.Throws<InvalidDataException>(() => new ObjectReadTable(new Dictionary<ObjectId, object> { [new(1)] = 1 }));
    }

    [Fact]
    public void ArrayConstraintsAreExactAndRecordLayoutCannotMixKinds() {
        ArrayLayout layout = new(TypeExprKind.VectorArray, new DurableFieldInfo(19, TypeTag.Int32));
        ArrayLayout equal = new(TypeExprKind.VectorArray, new DurableFieldInfo(1, TypeTag.Int32));
        Assert.Equal(layout, equal);
        Assert.Equal(1, layout.ElementSlot.FieldId);
        Assert.Equal(TypeExpr.VectorArray(TypeExpr.Builtin(TypeTag.Int32)), layout.Type);
        Assert.True(StateReferenceValidator.Accepts(ObjectLayout.ForArray(layout), layout.Type));
        Assert.False(StateReferenceValidator.Accepts(ObjectLayout.ForArray(layout), TypeExpr.MultiDimArray(TypeExpr.Builtin(TypeTag.Int32), 2)));
        Assert.False(StateReferenceValidator.Accepts(ObjectLayout.ForArray(layout), TypeExpr.Named("owner")));
        Assert.Null(ObjectLayout.ForArray(layout).Schema);
        Assert.Null(ObjectLayout.String.Array);
        Assert.Throws<ArgumentException>(() => StateReferenceValidator.RequireReferenceType(TypeExpr.Builtin(TypeTag.Int32)));
        Assert.Throws<ArgumentException>(() => StateReferenceValidator.RequireReferenceType(TypeExpr.VectorArray(TypeExpr.Parameter(0))));

        ObjectReadTable table = new(new Dictionary<ObjectId, object> { [new(1)] = new string[0] });
        Assert.Throws<InvalidDataException>(() => table.ResolveObject<object[]>(new(1)));
    }

    private sealed class Root : IDurableObject { }
}
