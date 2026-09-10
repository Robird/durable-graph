namespace Atelia.DurableGraph;

/// <summary>Supplies generated inline history to a referencing model compilation.</summary>
/// <remarks>
/// Generated build metadata only. A referencing generator reads this attribute without
/// executing the assembly. It does not register runtime definitions or historical readers.
/// The execution contract version is independent of the history document's format version.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DurableSchemaExportAttribute : Attribute {
    public DurableSchemaExportAttribute(int contractVersion, string schemaId, int schemaVersion, string manifest) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        ContractVersion = contractVersion;
        SchemaId = schemaId;
        SchemaVersion = schemaVersion;
        Manifest = manifest;
    }

    public int ContractVersion { get; }
    public string SchemaId { get; }
    public int SchemaVersion { get; }
    public string Manifest { get; }
}
