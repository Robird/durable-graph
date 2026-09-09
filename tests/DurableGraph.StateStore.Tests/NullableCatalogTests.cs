using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class NullableCatalogTests {
    [Theory]
    [InlineData(typeof(int?))]
    [InlineData(typeof(double?))]
    [InlineData(typeof(int?[]))]
    [InlineData(typeof(int?[,,,]))]
    [InlineData(typeof(List<int?>))]
    [InlineData(typeof(List<double?[]>))]
    public void CurrentNominalCompositionsRoundTripAndUseExactNullableState(Type domain) {
        StateModelSnapshot snapshot = new StateModelRegistry().Snapshot();
        TypeExpr type = snapshot.GetTypeExpr(domain);
        Assert.Equal(domain, snapshot.GetDomainType(type));
        StateValueBinding binding = snapshot.ResolveCurrentValue(domain);
        Assert.Same(binding, snapshot.ResolveCurrentValue(domain));
        if (Nullable.GetUnderlyingType(domain) is { } child) {
            Assert.Equal(TypeTag.Nullable, binding.Slot.TypeTag);
            Assert.Equal(typeof(NullableState<>).MakeGenericType(child), binding.StateType);
            Assert.Equal(domain, binding.DomainType);
            Assert.Equal(1, binding.Slot.NullableLayout!.ElementSlot.FieldId);
        }
        else {
            Assert.Equal(typeof(ObjectId), binding.StateType);
            Assert.True(snapshot.TryGetCurrentObjectBinding(domain, out ObjectBinding? value));
            Assert.Equal(type, value!.CurrentLayout.Type);
            Assert.Equal(value.CurrentLayout, snapshot.ResolveObjectReader(value.CurrentLayout).Layout);
        }
    }

    [Theory]
    [InlineData(typeof(decimal?))]
    [InlineData(typeof(DayOfWeek?))]
    [InlineData(typeof(DateTime?))]
    [InlineData(typeof(List<decimal?>))]
    public void UnsupportedNullableChildrenFailClosed(Type domain) {
        StateModelSnapshot snapshot = new StateModelRegistry().Snapshot();
        Assert.Throws<InvalidDataException>(() => snapshot.GetTypeExpr(domain));
        Assert.Throws<InvalidDataException>(() => snapshot.ResolveCurrentValue(domain));
    }

    [Fact]
    public void IndependentGoldenUsesNullableTypeConstructorAndRecursiveSlotWithoutChildFieldId() {
        TypeExpr type = TypeExpr.Named("B", TypeExpr.Nullable(TypeExpr.Named("P")),
            TypeExpr.List(TypeExpr.Nullable(TypeExpr.Builtin(TypeTag.Int32))));
        byte[] nominalGolden = Convert.FromHexString("02034202090203500008090102");
        Assert.Equal(nominalGolden, WriteType(type));
        Assert.Equal(type, ReadType(nominalGolden));
        for (int i = 0; i < nominalGolden.Length; i++) {
            byte[] prefix = nominalGolden[..i];
            Exception? error = Record.Exception(() => ReadType(prefix));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Prefix {i}: {error}");
        }

        DurableSchema owner = new("O", 1, DurableFieldInfo.Nullable(7, new(1, TypeTag.Int32)));
        byte[] ownerGolden = Convert.FromHexString("0201020102034F00010001071202");
        Assert.Equal(ownerGolden, SchemaCatalogTestData.Write([owner]));
        Assert.Equal(owner, SchemaCatalogTestData.Read(ownerGolden)[new("O", 1)]);
        SchemaCatalogEntry list = SchemaCatalogEntry.ForList(new(2), new(DurableFieldInfo.Nullable(1, new(1, TypeTag.Int32))));
        byte[] listGolden = Convert.FromHexString("02010204021202");
        Assert.Equal(listGolden, SchemaCatalogWireCodec.Write([list], CatalogTestData.Empty));
        Assert.Equal(list.Layout, Assert.Single(SchemaCatalogWireCodec.Read(listGolden, CatalogTestData.Empty)).Layout);
        for (int i = 0; i < listGolden.Length; i++) {
            byte[] prefix = listGolden[..i];
            Exception? error = Record.Exception(() => SchemaCatalogWireCodec.Read(prefix, CatalogTestData.Empty));
            Assert.True(error is InvalidDataException or EndOfStreamException, $"Catalog prefix {i}: {error}");
        }
    }

    [Fact]
    public void InlineNullableDependencyUsesEarlierIntegerIdAndColdCatalogPreservesItsVersion() {
        DurableSchema point = Point(1);
        DurableSchema owner = new("O", 1, Slot(point));
        byte[] golden = Convert.FromHexString("02020202020350000100010102030102034F0001000101121002");
        Assert.Equal(golden, SchemaCatalogTestData.Write([point, owner]));
        SchemaCatalogEntry[] rows = SchemaCatalogWireCodec.Read(golden, CatalogTestData.Empty);
        Assert.Same(rows[0].Schema, rows[1].Schema!.Fields[0].NullableLayout!.ElementSlot.InlineSchema);
        Assert.Same(rows[0].Schema, rows[1].Schema!.Fields[0].ValueSchema);

        string path = NewPath();
        try {
            RepresentationId[] ids;
            ObjectLayout list = ObjectLayout.ForList(new(Slot(point)));
            ObjectLayout array = ObjectLayout.ForArray(new(TypeExprKind.Rank2Array, Slot(point)));
            using (IRbfFile file = RbfFile.CreateNew(path)) {
                SchemaStore store = new(file);
                ids = store.RegisterRepresentations([ObjectLayout.ForDurable(owner), list, array]);
                Assert.Equal(new uint[] { 3, 4, 5 }, ids.Select(id => id.Value));
                Assert.Equal(2, store.Count);
                long tail = file.TailOffset;
                Assert.Equal(ids, store.RegisterRepresentations([ObjectLayout.ForDurable(owner), list, array]));
                Assert.Equal(tail, file.TailOffset);
                DurableSchema bad = new("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Byte));
                Assert.Throws<SchemaConflictException>(() => store.RegisterRepresentations([
                    ObjectLayout.ForList(new(new(1, TypeTag.Boolean))), ObjectLayout.ForArray(new(TypeExprKind.VectorArray, Slot(bad)))]));
                Assert.Equal(tail, file.TailOffset);
                Assert.Equal(2, store.Count);
                Assert.Equal(6U, store.RegisterRepresentations([ObjectLayout.ForList(new(new(1, TypeTag.Boolean)))])[0].Value);
            }
            using IRbfFile reopened = RbfFile.OpenReadOnlyExisting(path);
            SchemaStore cold = new(reopened, readOnly: true);
            Assert.Equal(list, cold.GetRepresentation(ids[1]));
            Assert.Equal(array, cold.GetRepresentation(ids[2]));
            Assert.Same(cold.GetRequired("P", 1), cold.GetRepresentation(ids[1]).List!.ElementSlot.ValueSchema);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("010102040202")] // Retired SCB1 v1, otherwise valid.
    [InlineData("02010204021204")] // Nullable string.
    [InlineData("0201020402120F02035000")] // Nullable reference.
    [InlineData("0201020402121202")] // Nested Nullable.
    [InlineData("02010204021211")] // History-only parameter tag.
    [InlineData("02010204021200")] // Unknown child.
    [InlineData("0201020402121001")] // String ID is not an inline Schema.
    [InlineData("0201020402121002")] // Self dependency.
    [InlineData("0201020402121003")] // Forward dependency.
    public void MalformedNullableCatalogSlotsAndRetiredVersionAreRejected(string hex) {
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(Convert.FromHexString(hex), CatalogTestData.Empty));
    }

    [Theory]
    [InlineData("090104")] // string.
    [InlineData("09040102")] // array.
    [InlineData("09080102")] // List.
    [InlineData("09090102")] // Nullable.
    [InlineData("090300")] // Open parameter.
    public void InvalidNullableNominalOperandsAreRejected(string hex) {
        Assert.Throws<InvalidDataException>(() => ReadType(Convert.FromHexString(hex)));
    }

    [Fact]
    public void RetainedNullableValuesNeedNoDomainAndCacheTheirCompleteChildLayout() {
        StateModelRegistry registry = RetainedPoints();
        StateModelSnapshot snapshot = registry.Snapshot();
        StateValueBinding first = snapshot.ResolveStoredValue(Slot(Point(1)));
        StateValueBinding second = snapshot.ResolveStoredValue(Slot(Point(2)));
        Assert.Equal(typeof(NullableState<int>), first.StateType);
        Assert.Equal(typeof(NullableState<long>), second.StateType);
        Assert.NotEqual(first.Slot, second.Slot);
        Assert.Null(first.DomainType);
        Assert.Null(second.ProjectionType);
        Assert.Equal(typeof(int), snapshot.ResolveStoredValue(first.Slot.NullableLayout!.ElementSlot).StateType);
        Assert.Equal(first.Slot, snapshot.ResolveStoredValue(first.Slot).Slot);
        Assert.Equal(27, snapshot.ResolveStoredValue(DurableFieldInfo.Nullable(27, first.Slot.NullableLayout!.ElementSlot)).Slot.FieldId);
        foreach (DurableSchema exact in new[] { Point(1), Point(2) }) {
            ObjectLayout layout = ObjectLayout.ForList(new(Slot(exact)));
            Assert.Equal(layout, snapshot.ResolveObjectReader(layout).Layout);
            Assert.Throws<InvalidDataException>(() => snapshot.GetDomainType(layout.Type));
        }
    }

    [Fact]
    public void CachedNullableValuesAndEmptyContainerReadersRecheckLateChildConflict() {
        string path = NewPath();
        try {
            using IRbfFile file = RbfFile.CreateNew(path);
            SchemaStore schemas = new(file);
            StateModelSnapshot snapshot = RetainedPoints().Snapshot(schemas);
            DurableFieldInfo slot = Slot(Point(1));
            ObjectLayout list = ObjectLayout.ForList(new(slot));
            ObjectLayout array = ObjectLayout.ForArray(new(TypeExprKind.VectorArray, slot));
            snapshot.ResolveStoredValue(slot);
            snapshot.ResolveObjectReader(list);
            snapshot.ResolveObjectReader(array);
            schemas.Register(new DurableSchema("P", 1, SchemaKind.InlineValue, new DurableFieldInfo(1, TypeTag.Byte)));
            Assert.Throws<InvalidDataException>(() => snapshot.ResolveStoredValue(slot));
            Assert.Throws<InvalidDataException>(() => snapshot.ResolveObjectReader(list));
            Assert.Throws<InvalidDataException>(() => snapshot.ResolveObjectReader(array));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableNominalChildEstablishesValueKindInEitherRegistrationOrder(bool ownerFirst) {
        DurableSchema owner = new("O", 1, DurableFieldInfo.Reference(1,
            TypeExpr.List(TypeExpr.Nullable(TypeExpr.Named("P")))));
        DurableSchema wrongKind = new("P", 1);
        SchemaCatalogEntry[] rows = CatalogTestData.Schemas(ownerFirst ? [owner, wrongKind] : [wrongKind, owner]);
        Assert.Throws<ArgumentException>(() => SchemaCatalogWireCodec.Write(rows, CatalogTestData.Empty));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(CatalogTestData.Encode(rows), CatalogTestData.Empty));
    }

    [Fact]
    public void NullableWrapperKeepsExactSchemaDepthAndWireDepthLimits() {
        DurableSchema[] chain = new DurableSchema[257];
        for (int i = 0; i < chain.Length; i++) {
            chain[i] = new($"P{i}", 1, SchemaKind.InlineValue,
                i == 0 ? new(1, TypeTag.Int32) : Slot(chain[i - 1]));
        }
        SchemaCatalogEntry[] registered = SchemaCatalogWireCodec.Read(SchemaCatalogTestData.Write(chain[..256]), CatalogTestData.Empty);
        Assert.Equal(256, registered.Length);
        Assert.Throws<ArgumentException>(() => SchemaCatalogTestData.Write(chain));
        Assert.Throws<InvalidDataException>(() => SchemaCatalogWireCodec.Read(
            CatalogTestData.Encode(CatalogTestData.Schemas(chain)), CatalogTestData.Empty));
        SchemaCatalogEntry list = SchemaCatalogEntry.ForList(new(258), new(Slot(chain[255])));
        var index = CatalogTestData.Index(registered);
        Assert.Equal(list.Layout, Assert.Single(SchemaCatalogWireCodec.Read(
            SchemaCatalogWireCodec.Write([list], index), index)).Layout);

        TypeExpr type = TypeExpr.Nullable(TypeExpr.Builtin(TypeTag.Int32));
        for (int i = 2; i < TypeExpr.MaximumDepth; i++) { type = TypeExpr.List(type); }
        Assert.Equal(type, ReadType(WriteType(type)));
        byte[] tooDeep = [.. Enumerable.Repeat((byte)8, TypeExpr.MaximumDepth - 1), 9, 1, 2];
        Assert.Throws<InvalidDataException>(() => ReadType(tooDeep));
    }

    private static StateModelRegistry RetainedPoints() {
        StateModelRegistry registry = new();
        registry.Register(new StateDefinitionBinding("P", SchemaKind.InlineValue, 0, null,
            [new("P", 1, SchemaKind.InlineValue, 0, [new(1, TypeExpr.Builtin(TypeTag.Int32))]),
             new("P", 2, SchemaKind.InlineValue, 0, [new(1, TypeExpr.Builtin(TypeTag.Int64))])],
            historicalValueFactory: static (schema, _) => new(new(1, TypeTag.InlineValue, inlineSchema: schema),
                schema.Version == 1 ? typeof(int) : typeof(long),
                schema.Version == 1 ? typeof(Int32StateOps) : typeof(Int64StateOps))));
        return registry;
    }

    private static DurableSchema Point(int version) => new("P", version, SchemaKind.InlineValue,
        new DurableFieldInfo(1, version == 1 ? TypeTag.Int32 : TypeTag.Int64));
    private static DurableFieldInfo Slot(DurableSchema child) => DurableFieldInfo.Nullable(1, new(1, TypeTag.InlineValue, inlineSchema: child));
    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"nullable-catalog-{Guid.NewGuid():N}.rbf");
    private static byte[] WriteType(TypeExpr type) {
        ArrayBufferWriter<byte> bytes = new();
        BinaryPayloadWriter writer = new(bytes);
        TypeExprWireCodec.Write(ref writer, type);
        return bytes.WrittenSpan.ToArray();
    }
    private static TypeExpr ReadType(byte[] bytes) {
        BinaryPayloadReader reader = new(bytes);
        TypeExpr type = TypeExprWireCodec.Read(ref reader);
        reader.EnsureFullyConsumed();
        return type;
    }
}
