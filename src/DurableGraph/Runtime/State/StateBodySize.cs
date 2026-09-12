using Atelia.DurableGraph.Schema;

namespace Atelia.DurableGraph.Runtime;

/// <summary>Lower bounds used before allocating owned sequence buffers. Empty structs may encode zero bytes.</summary>
internal static class StateBodySize {
    internal static int MinimumBaseBytes(DurableFieldInfo slot) =>
        MinimumBaseBytes(slot, new Dictionary<DurableSchema, int>(ReferenceEqualityComparer.Instance));

    private static int MinimumBaseBytes(DurableFieldInfo slot, Dictionary<DurableSchema, int> memo) {
        if (slot.TypeTag != TypeTag.InlineValue) {
            return slot.TypeTag switch {
                TypeTag.Half => 2, TypeTag.Single => 4, TypeTag.Double => 8,
                TypeTag.Guid or TypeTag.Decimal => 16, TypeTag.DateTimeOffset => 2, _ => 1,
            };
        }
        DurableSchema schema = slot.InlineSchema!;
        if (memo.TryGetValue(schema, out int known)) { return known; }
        long count = 0;
        foreach (DurableFieldInfo field in schema.Fields) {
            count += MinimumBaseBytes(field, memo);
            if (count >= int.MaxValue) { count = int.MaxValue; break; }
        }
        return memo[schema] = (int)count;
    }
}
