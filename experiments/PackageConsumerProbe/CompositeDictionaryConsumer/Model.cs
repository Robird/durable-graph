using Atelia.DurableGraph;
#if HISTORY_V1
using Part = CompositeDictionaryPackageConsumerProbe.LegacyPart;
using Key = CompositeDictionaryPackageConsumerProbe.LegacyKey<CompositeDictionaryPackageConsumerProbe.LegacyPart>;
using Value = CompositeDictionaryPackageConsumerProbe.LegacyValue;
#else
using Part = CompositeDictionaryPackageConsumerProbe.CurrentPart;
using Key = CompositeDictionaryPackageConsumerProbe.CurrentKey<CompositeDictionaryPackageConsumerProbe.CurrentPart>;
using Value = CompositeDictionaryPackageConsumerProbe.CurrentValue;
using PartStates = Atelia.DurableGraph.Generated.Family_50617274;
using KeyStates = Atelia.DurableGraph.Generated.Family_4B6579;
using ValueStates = Atelia.DurableGraph.Generated.Family_56616C7565;
#endif

namespace CompositeDictionaryPackageConsumerProbe;

#if HISTORY_V1
[DurableType("Part", 1)]
public partial struct LegacyPart : IEquatable<LegacyPart> {
    [DurableField(1)] public int Number;
    public bool Equals(LegacyPart other) => Number == other.Number;
    public override bool Equals(object? other) => other is LegacyPart part && Equals(part);
    public override int GetHashCode() => Number.GetHashCode();
}
[DurableType("Key", 1)]
public partial struct LegacyKey<T> : IEquatable<LegacyKey<T>> where T : struct, IEquatable<T> {
    [DurableField(1)] public T Part;
    [DurableField(2)] public int Scope;
    [DurableField(3)] public long Timestamp;
    [Transient] public int Scratch;
    public bool Equals(LegacyKey<T> other) => Part.Equals(other.Part) && Scope == other.Scope;
    public override bool Equals(object? other) => other is LegacyKey<T> key && Equals(key);
    public override int GetHashCode() => HashCode.Combine(Part, Scope);
}
[DurableType("Value", 1)]
public partial struct LegacyValue {
    [DurableField(1)] public int Number;
}
#else
// All three old CLR declarations are absent, independently of their retained generated history.
[DurableType("Part", 2)]
public partial struct CurrentPart : IEquatable<CurrentPart> {
    [DurableField(1)] public long Number;
    public bool Equals(CurrentPart other) => Number == other.Number;
    public override bool Equals(object? other) => other is CurrentPart part && Equals(part);
    public override int GetHashCode() => Number.GetHashCode();
}
[DurableType("Key", 2)]
public partial struct CurrentKey<T> : IEquatable<CurrentKey<T>> where T : struct, IEquatable<T> {
    [DurableField(1)] public T Part;
    [DurableField(2)] public int Scope;
    [DurableField(3)] public long Timestamp;
    [Transient] public int Scratch;
    public bool Equals(CurrentKey<T> other) => Part.Equals(other.Part) && Scope == other.Scope;
    public override bool Equals(object? other) => other is CurrentKey<T> key && Equals(key);
    public override int GetHashCode() => HashCode.Combine(Part, Scope);
}
[DurableType("Value", 2)]
public partial struct CurrentValue {
    [DurableField(1)] public long Number;
}
#endif

public sealed class ForwardComparer<T> : IEqualityComparer<T> {
    public bool Equals(T? left, T? right) => EqualityComparer<T>.Default.Equals(left!, right!);
    public int GetHashCode(T value) => EqualityComparer<T>.Default.GetHashCode(value!);
}

public sealed class CustomCaseComparer : IEqualityComparer<string> {
    public bool Equals(string? left, string? right) => StringComparer.OrdinalIgnoreCase.Equals(left, right);
    public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value);
}

[DurableType("World", 1)]
public partial class World : DurableBase {
    [DurableField(1)] public Dictionary<Key, Value> Rows = new();
    [DurableField(2)] public Dictionary<Key, Value> Alias = new();
    [DurableField(3)] public Dictionary<Key, Value> Application = new(new ForwardComparer<Key>());
    [DurableField(4)] public List<Dictionary<Key, Value>> Nested = [];
    [DurableField(5)] public Dictionary<string, int> Typed = new(new CustomCaseComparer());

    public static World Seed() {
        var world = new World();
        for (int i = 0; i < 32; i++) {
            var key = new Key { Part = new Part { Number = 100 + i }, Scope = i % 2, Timestamp = 10000 + i, Scratch = 9 };
            world.Rows.Add(key, new Value { Number = 200 + i });
            world.Application.Add(key, new Value { Number = 400 + i });
        }
        world.Alias = world.Rows;
        world.Nested = [world.Application, world.Rows, world.Application];
        world.Typed.Add(new("MiXeD".ToCharArray()), 41);
        return world;
    }
}

// Only current Equals/Hash changes between executables. Schema/version and bytes stay the same.
[DurableType("CollisionKey", 1)]
public partial struct CollisionKey : IEquatable<CollisionKey> {
    [DurableField(1)] public int Tenant;
    [DurableField(2)] public int Number;
    [DurableField(3)] public long Timestamp;
    public static int Comparisons;
    public bool Equals(CollisionKey other) {
        Comparisons++;
#if HISTORY_V1
        return Tenant == other.Tenant && Number == other.Number;
#else
        return Tenant == other.Tenant;
#endif
    }
    public override bool Equals(object? other) => other is CollisionKey key && Equals(key);
    public override int GetHashCode() {
        Comparisons++;
#if HISTORY_V1
        return HashCode.Combine(Tenant, Number);
#else
        return Tenant.GetHashCode();
#endif
    }
}

[DurableType("CollisionWorld", 1)]
public partial class CollisionWorld : DurableBase {
    [DurableField(1)] public Dictionary<CollisionKey, int> Rows = new();
}

#if HISTORY_V2
[ValueUpgradeRuleSet]
internal sealed class KeyRules { }
[ValueUpgradeRuleSet]
internal sealed class ValueRules { }

internal static class Upgrades {
    internal static readonly List<(ObjectId Id, int? Count, string Side)> Calls = [];

    [DurableValueUpgrade(typeof(KeyRules), "Part", 1, 2)]
    internal static void PartV1ToV2(in PartStates.V1 old, out PartStates.V2 next, UpgradeContext context) {
        Calls.Add((context.ObjectId, context.DictionaryCount, "part"));
        next = new(old.Segment0Field1 + 1000L);
    }

    [DurableValueUpgrade(typeof(KeyRules), "Key", 1, 2)]
    [UpgradeDependency("part", typeof(KeyRules), "Key", 1, "Key", 1)]
    internal static void KeyV1ToV2<A, B>(in KeyStates.V1<A> old, out KeyStates.V2<B> next, UpgradeContext context)
        where A : unmanaged where B : unmanaged {
        Calls.Add((context.ObjectId, context.DictionaryCount, "key"));
        var convert = context.GetValueUpgrade<A, B>("part");
        next = new(convert(in old.Segment0Field1), old.Segment0Field2, old.Segment0Field3);
    }

    [DurableValueUpgrade(typeof(ValueRules), "Value", 1, 2)]
    internal static void ValueV1ToV2(in ValueStates.V1 old, out ValueStates.V2 next, UpgradeContext context) {
        Calls.Add((context.ObjectId, context.DictionaryCount, "value"));
        next = new(old.Segment0Field1 + 1000L);
    }
}
#endif
