using Atelia.DurableGraph.Runtime;
namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    [Fact]
    public void GeneratedReadonlyHydratePreservesAllThirteenScalarRepresentations() {
        GeneratorTestRun run = RunGenerator("""
            using System;
            using Atelia.DurableGraph;
            using Atelia.DurableGraph.Schema;
            using Atelia.DurableGraph.Runtime;
            namespace ScalarRestore;
            [DurableType("restore.scalars", 1)]
            public sealed partial class World : IDurableObject {
                [DurableField(1)] private readonly bool _bool = true;
                [DurableField(2)] private readonly byte _byte = byte.MaxValue;
                [DurableField(3)] private readonly sbyte _sbyte = sbyte.MinValue;
                [DurableField(4)] private readonly short _short = short.MinValue;
                [DurableField(5)] private readonly ushort _ushort = ushort.MaxValue;
                [DurableField(6)] private readonly int _int = int.MinValue;
                [DurableField(7)] private readonly uint _uint = uint.MaxValue;
                [DurableField(8)] private readonly long _long = long.MinValue;
                [DurableField(9)] private readonly ulong _ulong = ulong.MaxValue;
                [DurableField(10)] private readonly char _char = '\uD800';
                [DurableField(11)] private readonly Half _half = BitConverter.Int16BitsToHalf(unchecked((short)0xFE01));
                [DurableField(12)] private readonly float _float = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
                [DurableField(13)] private readonly double _double = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000001));
                public static StateModelBinding Binding => __DurableState.Model;
                public static CapturedGraph Seed() {
                    var session = new CaptureSession();
                    using var context = session.BeginCapture();
                    __DurableState.AddRoot(context, new World());
                    return context.Seal();
                }
            }
            """);
        AssertSchemaOnlyCompiles(run);
        Type type = EmitAndLoad(run.OutputCompilation).GetType("ScalarRestore.World")!;
        StateModelBinding model = (StateModelBinding)type.GetProperty("Binding")!.GetValue(null)!;
        CapturedGraph seed = type.GetMethod("Seed")!.CreateDelegate<Func<CapturedGraph>>()();
        ObjectStateRecord expected = Assert.Single(seed.Objects);
        IDurableObject instance = model.Allocate();
        model.Hydrate(instance, model.Normalize(expected), new ObjectReadTable(StringReadTable.FromDecoded([]), new Dictionary<ObjectId, IDurableObject>()));
        CaptureSession session = new();
        using CaptureContext context = session.BeginCapture();
        model.AddRoot(context, instance);
        CapturedGraph captured = context.Seal();
        ObjectStateRecord actual = Assert.Single(captured.Objects);
        Assert.Equal(expected.Preparation!.PrepareBase(expected).Body.ToArray(),
            actual.Preparation!.PrepareBase(actual).Body.ToArray());
        Assert.False(actual.Preparation.PrepareDelta(expected, actual).HasChanges);
    }
}
