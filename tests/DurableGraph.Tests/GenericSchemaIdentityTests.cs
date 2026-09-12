using Atelia.DurableGraph.Schema;
namespace Atelia.DurableGraph.Tests;

public sealed class GenericSchemaIdentityTests {
    [Fact]
    public void NominalArgumentsAreImmutableStructuralIdentityAndDoNotCarryVersions() {
        TypeExpr[] arguments = [TypeExpr.Builtin(TypeTag.Int32), TypeExpr.Named("Point")];
        TypeExpr first = TypeExpr.Named("Box", arguments);
        arguments[0] = TypeExpr.Builtin(TypeTag.String);
        TypeExpr equal = TypeExpr.Named("Box", TypeExpr.Builtin(TypeTag.Int32), TypeExpr.Named("Point"));
        Assert.Equal(equal, first);
        Assert.Equal(equal.GetHashCode(), first.GetHashCode());
        Assert.NotEqual(TypeExpr.Named("Box", TypeExpr.Named("Point"), TypeExpr.Builtin(TypeTag.Int32)), first);
        Assert.NotEqual(TypeExpr.Named("Box"), first);
        Assert.Equal(TypeTag.Int32, first.Arguments[0].BuiltinTag);
        Assert.True(first.IsClosed);
    }

    [Fact]
    public void OpenPatternsCanRepeatParametersButCannotIdentifyAnExactSchemaOrReference() {
        TypeExpr pattern = TypeExpr.Named("Pair", TypeExpr.Parameter(0), TypeExpr.Parameter(0));
        Assert.False(pattern.IsClosed);
        Assert.Equal(TypeExpr.Parameter(0), pattern.Arguments[1]);
        Assert.Throws<ArgumentException>(() => new DurableSchema(pattern, 1));
        Assert.Throws<ArgumentException>(() => DurableFieldInfo.Reference(1, pattern));
        Assert.Throws<ArgumentException>(() => new DurableSchema(TypeExpr.Builtin(TypeTag.Int32), 1));
        Assert.Throws<ArgumentException>(() => DurableFieldInfo.Reference(1, TypeExpr.Builtin(TypeTag.String)));
    }

    [Fact]
    public void ExactLayoutIncludesClosedNominalArgumentsAndVersionedInlineDependencies() {
        DurableSchema point1 = new("Point", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
        DurableSchema point2 = new("Point", 2, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64));
        TypeExpr closed = TypeExpr.Named("Box", TypeExpr.Named("Point"));
        DurableSchema old = new(closed, 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point1));
        DurableSchema missedBump = new(closed, 1, new DurableFieldInfo(1, TypeTag.InlineValue, inlineSchema: point2));
        Assert.Equal(old.Type, missedBump.Type);
        Assert.NotEqual(old, missedBump);
        Assert.NotEqual(new DurableSchema(TypeExpr.Named("Box", TypeExpr.Builtin(TypeTag.Int32)), 1),
            new DurableSchema(TypeExpr.Named("Box", TypeExpr.Builtin(TypeTag.String)), 1));
        Assert.Equal(new DurableSchema("Plain", 1), new DurableSchema(TypeExpr.Named("Plain"), 1));
    }

    [Fact]
    public void EqualUInt32StorageDoesNotEraseThreeDifferentFieldSemantics() {
        DurableFieldInfo number = new(1, TypeTag.UInt32);
        DurableFieldInfo text = new(1, TypeTag.String);
        DurableFieldInfo reference = DurableFieldInfo.Reference(1, TypeExpr.Named("Box", TypeExpr.Builtin(TypeTag.String)));
        Assert.NotEqual(number, text);
        Assert.NotEqual(text, reference);
        Assert.NotEqual(reference, DurableFieldInfo.Reference(1, TypeExpr.Named("Box", TypeExpr.Builtin(TypeTag.UInt32))));
        Assert.Equal("Box", reference.TargetSchemaId);
        Assert.Equal(new DurableFieldInfo(1, TypeTag.ObjectReference, "Plain"),
            DurableFieldInfo.Reference(1, TypeExpr.Named("Plain")));
    }

    [Fact]
    public void TypeExpressionLimitsBoundBothDeepAndWideExpandedTrees() {
        TypeExpr expression = TypeExpr.Builtin(TypeTag.Int32);
        for (int depth = 1; depth < TypeExpr.MaximumDepth; depth++) { expression = TypeExpr.Named("N", expression); }
        Assert.Throws<ArgumentException>(() => TypeExpr.Named("N", expression));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeExpr.Named("N", new TypeExpr[TypeExpr.MaximumArity + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeExpr.Parameter(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeExpr.Parameter(TypeExpr.MaximumArity));
        TypeExpr wide = TypeExpr.Named("N", Enumerable.Repeat(TypeExpr.Builtin(TypeTag.Int32), 32).ToArray());
        TypeExpr shared = TypeExpr.Named("N", Enumerable.Repeat(wide, 32).ToArray());
        Assert.Throws<ArgumentException>(() => TypeExpr.Named("N", Enumerable.Repeat(shared, 4).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeExpr.Builtin(TypeTag.ObjectReference));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeExpr.Builtin(TypeTag.InlineValue));
    }
}
