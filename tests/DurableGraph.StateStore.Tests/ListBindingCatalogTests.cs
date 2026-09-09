using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class ListBindingCatalogTests {
    [Theory]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(List<List<int>>))]
    [InlineData(typeof(List<int[,]>))]
    [InlineData(typeof(List<string>[,,]))]
    [InlineData(typeof(List<List<int>[]>))]
    public void BuiltinCompositionsRoundTripAndBindObjectIdSlots(Type domain) {
        StateModelSnapshot snapshot = new StateModelRegistry().Snapshot();
        TypeExpr type = snapshot.GetTypeExpr(domain);
        Assert.Equal(domain, snapshot.GetDomainType(type));
        StateValueBinding slot = snapshot.ResolveCurrentValue(domain);
        Assert.Equal(typeof(ObjectId), slot.StateType);
        Assert.Equal(typeof(ObjectIdStateOps), slot.StateOpsType);
        Assert.Equal(type, slot.Slot.TargetType);
        Assert.True(snapshot.TryGetCurrentObjectBinding(domain, out ObjectBinding? binding));
        Assert.True(snapshot.TryGetCurrentObjectBinding(domain, out ObjectBinding? cached));
        Assert.Same(binding, cached);
        Assert.Equal(type, binding!.CurrentLayout.Type);
    }

    [Fact]
    public void ReferenceOperandsAndPhantomGenericArgumentsDoNotCloseBodies() {
        int factories = 0;
        StateModelRegistry registry = new();
        registry.Register(new StateDefinitionBinding("Box", SchemaKind.ReferenceObject, 1, typeof(Box<>),
            [new("Box", 1, SchemaKind.ReferenceObject, 1, [])],
            currentModelFactory: (_, _) => { factories++; throw new InvalidOperationException("Unexpected closure."); }));
        StateModelSnapshot snapshot = registry.Snapshot();
        Type type = typeof(List<Box<List<int>>>);
        Assert.Equal(type, snapshot.GetDomainType(snapshot.GetTypeExpr(type)));
        Assert.True(snapshot.TryGetCurrentObjectBinding(type, out _));
        Assert.Equal(0, factories);
    }

    [Fact]
    public void UnsupportedElementsOpenTypesInterfacesAndSubclassesFailClosed() {
        StateModelSnapshot snapshot = new StateModelRegistry().Snapshot();
        Type[] unsupported = [typeof(List<>), typeof(List<object>), typeof(List<decimal>),
            typeof(List<int?>), typeof(List<DayOfWeek>), typeof(IList<int>), typeof(DerivedList), typeof(List<DerivedList>)];
        foreach (Type type in unsupported) {
            Assert.Throws<InvalidDataException>(() => snapshot.GetTypeExpr(type));
            Exception? failure = Record.Exception(() => {
                if (snapshot.TryGetCurrentObjectBinding(type, out _)) {
                    throw new InvalidOperationException("Unsupported binding was accepted.");
                }
            });
            Assert.True(failure is null or InvalidDataException, $"{type}: {failure}");
        }
    }

    [Fact]
    public void HistoricalReaderOnlyNeedsRetainedStateAndRechecksLateAuthorityConflict() {
        string path = Path.Combine(Path.GetTempPath(), $"list-binding-{Guid.NewGuid():N}.rbf");
        try {
            using IRbfFile file = RbfFile.CreateNew(path);
            SchemaStore schemas = new(file);
            StateReaderRegistry registry = new();
            registry.Register(new StateDefinitionBinding("RetiredPoint", SchemaKind.InlineValue, 0, null,
                [new("RetiredPoint", 1, SchemaKind.InlineValue, 0, [new(1, TypeExpr.Builtin(TypeTag.Int32))])],
                historicalValueFactory: static (schema, _) => new(
                    new(1, TypeTag.InlineValue, inlineSchema: schema), typeof(int), typeof(Int32StateOps))));
            DurableSchema exact = new("RetiredPoint", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int32));
            ObjectLayout layout = ObjectLayout.ForList(new(new(1, TypeTag.InlineValue, inlineSchema: exact)));
            StateModelSnapshot snapshot = registry.Snapshot(schemas);
            ObjectReaderBinding reader = snapshot.ResolveObjectReader(layout);
            Assert.Same(reader, snapshot.ResolveObjectReader(layout));
            Assert.Equal(layout, reader.Layout);
            Assert.Throws<InvalidDataException>(() => snapshot.GetDomainType(layout.Type));
            schemas.Register(new DurableSchema("RetiredPoint", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Int64)));
            Assert.Throws<InvalidDataException>(() => snapshot.ResolveObjectReader(layout));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void RuleSelectionIsIndependentExplicitAndFrozen() {
        StateModelRegistry registry = new();
        StateModelSnapshot before = registry.Snapshot();
        Assert.Throws<InvalidOperationException>(() => registry.UseListElementUpgrades(typeof(ListRules)));
        registry.Register(new StateValueUpgradeRuleSet(typeof(ListRules), []));
        registry.Register(new StateValueUpgradeRuleSet(typeof(ArrayRules), []));
        registry.UseListElementUpgrades(typeof(ListRules));
        registry.UseListElementUpgrades(typeof(ListRules));
        registry.UseArrayElementUpgrades(typeof(ArrayRules));
        Assert.Throws<InvalidOperationException>(() => registry.UseListElementUpgrades(typeof(ArrayRules)));
        StateModelSnapshot after = registry.Snapshot();
        Assert.Null(before.ListElementUpgradeRuleSet);
        Assert.Equal(typeof(ListRules), after.ListElementUpgradeRuleSet);
        Assert.Equal(typeof(ArrayRules), after.ArrayElementUpgradeRuleSet);
    }

    [Fact]
    public void EmptyListReaderStillChecksNestedNominalKindAndArity() {
        StateModelRegistry registry = new();
        registry.Register(new StateDefinitionBinding("Point", SchemaKind.InlineValue, 0, null,
            [new("Point", 1, SchemaKind.InlineValue, 0, [])]));
        registry.Register(new StateDefinitionBinding("Node", SchemaKind.ReferenceObject, 0, null,
            [new("Node", 1, SchemaKind.ReferenceObject, 0, [])]));
        StateModelSnapshot snapshot = registry.Snapshot();
        TypeExpr[] invalid = [TypeExpr.Named("Point"), TypeExpr.Named("Missing"),
            TypeExpr.List(TypeExpr.Named("Missing")),
            TypeExpr.List(TypeExpr.Named("Point", TypeExpr.Builtin(TypeTag.Int32))),
            TypeExpr.Named("Node", TypeExpr.Builtin(TypeTag.Int32))];
        foreach (TypeExpr target in invalid) {
            ObjectLayout layout = ObjectLayout.ForList(new(DurableFieldInfo.Reference(1, target)));
            Assert.Throws<InvalidDataException>(() => snapshot.ResolveObjectReader(layout));
        }
        // The referenced inner List owns Point's exact version; the outer reader
        // only needs its nominal declaration, with no Point historical body.
        ObjectLayout valid = ObjectLayout.ForList(new(DurableFieldInfo.Reference(1, TypeExpr.List(TypeExpr.Named("Point")))));
        Assert.Equal(valid, snapshot.ResolveObjectReader(valid).Layout);
    }

    private sealed class Box<T> : DurableBase;
    private sealed class DerivedList : List<int>;
    private sealed class ListRules;
    private sealed class ArrayRules;
}
