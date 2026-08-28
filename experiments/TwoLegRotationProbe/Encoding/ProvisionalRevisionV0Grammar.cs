using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>One-byte semantic role of a provisional domain record.</summary>
internal enum ProvisionalDomainRecordRole : byte {
    Base = 1,
    Delta = 2,
    Relay = 3,
}

/// <summary>Size-only domain-record input. Synthetic payload is deliberately opaque.</summary>
internal readonly record struct ProvisionalDomainRecordInput(
    uint ObjectId,
    ProvisionalDomainRecordRole Role,
    int SyntheticPayloadBytes,
    RelativeFrameTicket? ParentFrameTicket);

/// <summary>One-byte semantic kind of the provisional object-version dictionary.</summary>
internal enum ProvisionalObjectVersionDictionaryKind : byte {
    Base = 1,
    Delta = 2,
}

/// <summary>Binding carried by one provisional object-version dictionary entry.</summary>
internal enum ProvisionalObjectVersionDictionaryBindingKind : byte {
    Self = 1,
    External = 2,
    Remove = 3,
}

internal readonly record struct ProvisionalObjectVersionDictionaryEntry(
    uint ObjectId,
    ProvisionalObjectVersionDictionaryBindingKind BindingKind,
    RelativeFrameTicket? ExternalFrameTicket) {
    public static ProvisionalObjectVersionDictionaryEntry BindSelf(uint objectId) =>
        new(objectId, ProvisionalObjectVersionDictionaryBindingKind.Self, null);

    public static ProvisionalObjectVersionDictionaryEntry BindExternal(
        uint objectId,
        RelativeFrameTicket frameTicket) =>
        new(objectId, ProvisionalObjectVersionDictionaryBindingKind.External, frameTicket);

    public static ProvisionalObjectVersionDictionaryEntry Remove(uint objectId) =>
        new(objectId, ProvisionalObjectVersionDictionaryBindingKind.Remove, null);
}

internal sealed class ProvisionalObjectVersionDictionaryInput {
    private readonly ReadOnlyCollection<ProvisionalObjectVersionDictionaryEntry> _entries;

    public ProvisionalObjectVersionDictionaryInput(
        ProvisionalObjectVersionDictionaryKind kind,
        RelativeFrameTicket? parentFrameTicket,
        IEnumerable<ProvisionalObjectVersionDictionaryEntry> entries) {
        ArgumentNullException.ThrowIfNull(entries);
        Kind = kind;
        ParentFrameTicket = parentFrameTicket;
        _entries = Array.AsReadOnly(entries.ToArray());
    }

    public ProvisionalObjectVersionDictionaryKind Kind { get; }

    public RelativeFrameTicket? ParentFrameTicket { get; }

    public IReadOnlyList<ProvisionalObjectVersionDictionaryEntry> Entries => _entries;
}

/// <summary>
/// Explicit size-only grammar input for one provisional revision. It has no dependency on
/// simulator frames, workload changes, or persisted object-version instances.
/// </summary>
internal sealed class ProvisionalRevisionV0Input {
    private readonly ReadOnlyCollection<ProvisionalDomainRecordInput> _domainRecords;

    public ProvisionalRevisionV0Input(
        IEnumerable<ProvisionalDomainRecordInput> domainRecords,
        ProvisionalObjectVersionDictionaryInput objectVersionDictionary) {
        ArgumentNullException.ThrowIfNull(domainRecords);
        ArgumentNullException.ThrowIfNull(objectVersionDictionary);
        _domainRecords = Array.AsReadOnly(domainRecords.ToArray());
        ObjectVersionDictionary = objectVersionDictionary;
    }

    public IReadOnlyList<ProvisionalDomainRecordInput> DomainRecords => _domainRecords;

    public ProvisionalObjectVersionDictionaryInput ObjectVersionDictionary { get; }
}
