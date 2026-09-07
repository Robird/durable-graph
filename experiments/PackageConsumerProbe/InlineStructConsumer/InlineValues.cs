using Atelia.DurableGraph;

namespace InlineStructPackageConsumerProbe;

#if HISTORY_V1
[DurableType("package.inline-point", 1)]
#else
[DurableType("package.inline-point", 2)]
#endif
public readonly partial struct Point {
#if HISTORY_V1
    [DurableField(1)] private readonly int _value;
    internal Point(int value, string label, Node node) {
#else
    [DurableField(1)] private readonly long _value;
    internal Point(long value, string label, Node node) {
#endif
        _value = value; _label = label; _node = node; _transient = 99;
    }
    [DurableField(2)] private readonly string _label;
    [DurableField(3)] private readonly Node _node;
    [Transient] private readonly int _transient;
    internal long Value => _value;
    internal string Label => _label;
    internal Node Node => _node;
    internal int TransientValue => _transient;
}

#if HISTORY_V1
[DurableType("package.inline-links", 1)]
#else
[DurableType("package.inline-links", 2)]
#endif
public readonly partial struct Links {
    [DurableField(1)] private readonly Point _left;
    [DurableField(2)] private readonly Point _right;
    [DurableField(3)] private readonly World _world;
    internal Links(Point left, Point right, World world) { _left = left; _right = right; _world = world; }
    internal Point Left => _left;
    internal Point Right => _right;
    internal World World => _world;
}
