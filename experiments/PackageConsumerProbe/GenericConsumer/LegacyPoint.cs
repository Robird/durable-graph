using Atelia.DurableGraph;

namespace GenericPackageConsumerProbe;

#if HISTORY_V1
[DurableType("GenericLegacyPoint", 1)]
#else
[DurableType("GenericLegacyPoint", 2)]
#endif
public readonly partial struct LegacyPoint {
#if HISTORY_V1
    [DurableField(1)] private readonly int _value;
    internal LegacyPoint(int value) { _value = value; }
#else
    [DurableField(1)] private readonly long _value;
    internal LegacyPoint(long value) { _value = value; }
#endif
    internal long Value => _value;
}
