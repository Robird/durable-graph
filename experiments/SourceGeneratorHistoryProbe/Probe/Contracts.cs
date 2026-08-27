namespace HistoryProbe;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class DurableAttribute : Attribute {
    public DurableAttribute(string schemaId, int version) {
        SchemaId = schemaId;
        Version = version;
    }

    public string SchemaId { get; }

    public int Version { get; }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
internal sealed class FieldAttribute : Attribute {
    public FieldAttribute(int fieldId) {
        FieldId = fieldId;
    }

    public int FieldId { get; }
}
