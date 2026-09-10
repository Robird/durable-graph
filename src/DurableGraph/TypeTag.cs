namespace Atelia.DurableGraph;

/// <summary>
/// Identifies a field type supported by the current durable schema model.
/// </summary>
public enum TypeTag {
    Invalid = 0,
    Boolean = 1,
    Int32 = 2,
    Int64 = 3,
    String = 4,
    Byte = 5,
    SByte = 6,
    Int16 = 7,
    UInt16 = 8,
    UInt32 = 9,
    UInt64 = 10,
    Char = 11,
    Half = 12,
    Single = 13,
    Double = 14,
    ObjectReference = 15,
    InlineValue = 16,
    // 17 is reserved for declaration parameters in retained build history.
    Nullable = 18,
    Guid = 19,
    Decimal = 20,
    TimeSpan = 21,
}

/// <summary>Classifies the fixed built-in leaf tags without including compound or history-only tags.</summary>
internal static class TypeTagFacts {
    internal static bool IsBuiltin(TypeTag tag) =>
        tag is >= TypeTag.Boolean and <= TypeTag.Double or TypeTag.Guid or TypeTag.Decimal or TypeTag.TimeSpan;
}
