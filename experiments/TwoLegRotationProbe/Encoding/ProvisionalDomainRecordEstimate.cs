namespace Atelia.TwoLegRotationProbe.Encoding;

internal readonly record struct ProvisionalDomainRecordEstimate(
    uint ObjectId,
    int RecordOffsetBytes,
    int SyntheticObjectPayloadBytes,
    int HeaderBytes,
    int FullRecordBytes,
    int DeltaParentTokenBytes);
