using Atelia.DurableGraph;
using Atelia.DurableGraph.Runtime;
#if HISTORY_V1
using Mode = EnumPackageConsumerProbe.LegacyMode;
#else
using Mode = EnumPackageConsumerProbe.CurrentMode;
using ModeStates = Atelia.DurableGraph.Generated.Family_4D6F6465;
using CellStates = Atelia.DurableGraph.Generated.Family_43656C6C;
using BoxStates = Atelia.DurableGraph.Generated.Family_426F78;
using WorldStates = Atelia.DurableGraph.Generated.Family_576F726C64;
#endif

namespace EnumPackageConsumerProbe;

#if HISTORY_V1
[Flags, DurableType("Mode", 1)]
public enum LegacyMode : int { Idle = 0, Moving = 1, Alias = 1 }
#else
// No LegacyMode declaration survives in V2. Stored DTOs depend only on retained history.
[DurableType("Mode", 2)]
public enum CurrentMode : long { Stopped = 0, Active = 1001 }
#endif

#if HISTORY_V1
[DurableType("Cell", 1)]
#else
[DurableType("Cell", 2)]
#endif
public partial struct Cell<T> where T : unmanaged, Enum {
    [DurableField(1)] public T Value;
}

#if HISTORY_V1
[DurableType("Box", 1)]
#else
[DurableType("Box", 2)]
#endif
public partial class Box<T> : IDurableObject where T : struct, Enum {
    [DurableField(1)] public T? Value;
}

#if HISTORY_V1
[DurableType("World", 1)]
#else
[DurableType("World", 2)]
#endif
public partial class World : IDurableObject {
    [DurableField(1)] public Mode Mode;
    [DurableField(2)] public Mode? Optional;
    [DurableField(3)] public List<Mode> Modes = [];
    [DurableField(4)] public List<Mode> Alias = [];
    [DurableField(5)] public Mode[] Vector = [];
    [DurableField(6)] public Mode?[,,,] Grid = new Mode?[0, 0, 0, 0];
    [DurableField(7)] public Box<Mode> Box = new();
    [DurableField(8)] public Cell<Mode> Cell;

    public static World Seed() {
        var world = new World {
            Mode = (Mode)(-17), Optional = (Mode)41,
            Modes = Enumerable.Range(100, 32).Select(value => (Mode)value).ToList(),
            Vector = [(Mode)int.MinValue, (Mode)int.MaxValue],
            Grid = new Mode?[1, 1, 1, 2],
            Box = new() { Value = (Mode)51 },
            Cell = new() { Value = (Mode)61 }
        };
        world.Alias = world.Modes;
        world.Grid[0, 0, 0, 1] = (Mode)81;
        return world;
    }
}

#if HISTORY_V2
[ValueUpgradeRuleSet(AllowNullableLifting = true)]
internal sealed class EnumRules { }

internal static class Upgrades {
    internal static readonly List<ObjectId> Calls = [];
    internal static int OwnerCalls;

    [DurableValueUpgrade(typeof(EnumRules), "Mode", 1, 2)]
    internal static void ModeV1ToV2(in ModeStates.V1 old, out ModeStates.V2 next, UpgradeContext context) {
        Calls.Add(context.ObjectId);
        next = new(old.Segment0Field1 + 1000L);
    }

    [DurableValueUpgrade(typeof(EnumRules), "Cell", 1, 2)]
    [UpgradeDependency("value", typeof(EnumRules), "Cell", 1, "Cell", 1)]
    internal static void CellV1ToV2<A, B>(in CellStates.V1<A> old, out CellStates.V2<B> next, UpgradeContext context)
        where A : unmanaged where B : unmanaged {
        var convert = context.GetValueUpgrade<A, B>("value");
        next = new(convert(in old.Segment0Field1));
    }

    [DurableUpgrade(typeof(Box<>), 1)]
    [UpgradeDependency("value", typeof(EnumRules), "Box", 1, "Box", 1)]
    internal static void BoxV1ToV2<A, B>(in BoxStates.V1<A> old, out BoxStates.V2<B> next, UpgradeContext context)
        where A : unmanaged where B : unmanaged {
        OwnerCalls++;
        var convert = context.GetValueUpgrade<A, B>("value");
        next = new(convert(in old.Segment0Field1));
    }

    [DurableUpgrade(typeof(World), 1)]
    [UpgradeDependency("mode", typeof(EnumRules), "World", 1, "World", 1)]
    [UpgradeDependency("optional", typeof(EnumRules), "World", 2, "World", 2)]
    [UpgradeDependency("cell", typeof(EnumRules), "World", 8, "World", 8)]
    internal static void WorldV1ToV2(
        in WorldStates.V1<NullableState<ModeStates.V1>, CellStates.V1<ModeStates.V1>> old,
        out WorldStates.V2<NullableState<ModeStates.V2>, CellStates.V2<ModeStates.V2>> next, UpgradeContext context) {
        OwnerCalls++;
        var mode = context.GetValueUpgrade<ModeStates.V1, ModeStates.V2>("mode");
        var optional = context.GetValueUpgrade<NullableState<ModeStates.V1>, NullableState<ModeStates.V2>>("optional");
        var cell = context.GetValueUpgrade<CellStates.V1<ModeStates.V1>, CellStates.V2<ModeStates.V2>>("cell");
        next = new(mode(in old.Segment0Field1), optional(in old.Segment0Field2), old.Segment0Field3,
            old.Segment0Field4, old.Segment0Field5, old.Segment0Field6, old.Segment0Field7, cell(in old.Segment0Field8));
    }
}
#endif
