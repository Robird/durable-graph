using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>Component sizing result for an explicitly provisional DurableGraph revision shape.</summary>
internal sealed class ProvisionalRevisionV0Estimate {
    private readonly ReadOnlyCollection<ProvisionalDomainRecordEstimate> _domainRecords;

    internal ProvisionalRevisionV0Estimate(
        IEnumerable<ProvisionalDomainRecordEstimate> domainRecords,
        int syntheticObjectPayloadBytes,
        int domainRecordHeaderBytes,
        int objectVersionDictionaryRecordBytes,
        int tailMetaDirectoryBytes,
        int addressTokenBytes,
        int objectVersionDictionaryPayloadOffsetBytes,
        RbfFrameLayoutEstimate rbfLayout) {
        ArgumentNullException.ThrowIfNull(domainRecords);
        _domainRecords = Array.AsReadOnly(domainRecords.ToArray());
        SyntheticObjectPayloadBytes = syntheticObjectPayloadBytes;
        DomainRecordHeaderBytes = domainRecordHeaderBytes;
        ObjectVersionDictionaryRecordBytes = objectVersionDictionaryRecordBytes;
        TailMetaDirectoryBytes = tailMetaDirectoryBytes;
        AddressTokenBytes = addressTokenBytes;
        ObjectVersionDictionaryPayloadOffsetBytes = objectVersionDictionaryPayloadOffsetBytes;
        RbfLayout = rbfLayout;
    }

    public IReadOnlyList<ProvisionalDomainRecordEstimate> DomainRecords => _domainRecords;

    public int DomainRecordCount => _domainRecords.Count;

    public int SyntheticObjectPayloadBytes { get; }

    public int DomainRecordHeaderBytes { get; }

    public int ObjectVersionDictionaryRecordBytes { get; }

    public int TailMetaDirectoryBytes { get; }

    public int AddressTokenBytes { get; }

    public int ObjectVersionDictionaryPayloadOffsetBytes { get; }

    public int PayloadLengthBytes => RbfLayout.PayloadLengthBytes;

    public RbfFrameLayoutEstimate RbfLayout { get; }
}
