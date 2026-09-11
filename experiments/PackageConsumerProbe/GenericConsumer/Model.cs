using Atelia.DurableGraph;

namespace GenericPackageConsumerProbe;

#if HISTORY_V1
[DurableType("GenericBox", 1)]
#elif HISTORY_V2
[DurableType("GenericBox", 2)]
#else
[DurableType("GenericBox", 3)]
#endif
public sealed partial class Box<T> : IDurableObject {
    [DurableField(1)] private T _value;
    [DurableField(4)] private readonly ulong _createdAtTicks;
    internal ulong CreatedAtTicks => _createdAtTicks;
#if !HISTORY_V1
    [DurableField(2)] private readonly int _stamp;
    internal int Stamp => _stamp;
#endif
#if HISTORY_V3
    [DurableField(3)] private readonly int _lastStamp;
    internal int LastStamp => _lastStamp;
#endif
    [Transient] private readonly int _transient = 77;
    internal int TransientValue => _transient;
    internal T Value => _value;
    internal Box(T value) { _value = value; _createdAtTicks = 638_625_600_000_000_000; }
    internal void Set(T value) => _value = value;
}

#if HISTORY_V1
[DurableType("GenericPoint", 1)]
#else
[DurableType("GenericPoint", 2)]
#endif
public readonly partial struct Point {
#if HISTORY_V1
    [DurableField(1)] private readonly int _value;
    internal Point(int value) { _value = value; }
#else
    [DurableField(1)] private readonly long _value;
    internal Point(long value) { _value = value; }
#endif
    internal long Value => _value;
}

#if HISTORY_V1
[DurableType("GenericPair", 1)]
#else
[DurableType("GenericPair", 2)]
#endif
public readonly partial struct Pair<T> {
    [DurableField(1)] private readonly T _left;
    [DurableField(2)] private readonly T _right;
    internal Pair(T left, T right) { _left = left; _right = right; }
    internal T Left => _left;
    internal T Right => _right;
}

#if HISTORY_V1
[DurableType("GenericWorld", 1)]
#elif HISTORY_V2
[DurableType("GenericWorld", 2)]
#else
[DurableType("GenericWorld", 3)]
#endif
public sealed partial class World : IDurableObject {
    [DurableField(1)] private readonly Box<int> _first;
    [DurableField(2)] private readonly Box<int> _second;
    [DurableField(3)] private readonly Box<Point> _point;
    [DurableField(4)] private readonly string _label;
#if HISTORY_V3
    [DurableField(5)] private readonly long _summary;
    internal long Summary => _summary;
#else
    [DurableField(5)] private readonly Pair<LegacyPoint> _legacy;
    internal Pair<LegacyPoint> Legacy => _legacy;
#endif
    internal Box<int> First => _first;
    internal Box<int> Second => _second;
    internal Box<Point> PointBox => _point;
    internal string Label => _label;
#if HISTORY_V1
    internal World() {
        _first = new(11);
        _second = new(21);
        _point = new(new Point(31));
        _label = new("generic history".ToCharArray());
        _legacy = new(new LegacyPoint(41), new LegacyPoint(51));
    }
#else
    private World(int unused) {
        throw new InvalidOperationException("Restore must not invoke domain constructors.");
    }
#endif
}
