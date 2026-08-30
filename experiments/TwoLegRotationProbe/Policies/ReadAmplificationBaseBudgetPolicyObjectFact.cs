namespace Atelia.TwoLegRotationProbe.Policies;

internal enum ReadAmplificationBaseBudgetPolicyObjectKind {
    Insert,
    Update,
    NoChange,
}

/// <summary>
/// Advisory object-payload facts for the prototype policy. These values are not
/// encoded-record or whole-candidate sizing authority.
/// </summary>
internal sealed class ReadAmplificationBaseBudgetPolicyObjectFact {
    internal ReadAmplificationBaseBudgetPolicyObjectFact(
        uint objectId,
        ReadAmplificationBaseBudgetPolicyObjectKind kind,
        bool isADependent,
        int postSaveBasePayloadBytes,
        long sourceHeadReconstructionObjectPayloadBytes,
        int? deltaPayloadBytes) {
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(postSaveBasePayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            sourceHeadReconstructionObjectPayloadBytes);

        long? readAmplificationNumeratorBytes;
        switch (kind) {
            case ReadAmplificationBaseBudgetPolicyObjectKind.Insert:
                if (isADependent ||
                    sourceHeadReconstructionObjectPayloadBytes != 0 ||
                    deltaPayloadBytes is not null) {
                    throw new ArgumentException(
                        "An Insert has no source dependency, source reconstruction, or Delta payload.");
                }

                readAmplificationNumeratorBytes = null;
                break;
            case ReadAmplificationBaseBudgetPolicyObjectKind.Update:
                if (deltaPayloadBytes is not > 0) {
                    throw new ArgumentException(
                        "An Update must have a positive Delta payload.",
                        nameof(deltaPayloadBytes));
                }

                readAmplificationNumeratorBytes = checked(
                    sourceHeadReconstructionObjectPayloadBytes +
                    deltaPayloadBytes.Value);
                break;
            case ReadAmplificationBaseBudgetPolicyObjectKind.NoChange:
                if (deltaPayloadBytes is not null) {
                    throw new ArgumentException(
                        "A NoChange object has no Delta payload.",
                        nameof(deltaPayloadBytes));
                }

                readAmplificationNumeratorBytes =
                    sourceHeadReconstructionObjectPayloadBytes;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ObjectId = objectId;
        Kind = kind;
        IsADependent = isADependent;
        PostSaveBasePayloadBytes = postSaveBasePayloadBytes;
        SourceHeadReconstructionObjectPayloadBytes =
            sourceHeadReconstructionObjectPayloadBytes;
        DeltaPayloadBytes = deltaPayloadBytes;
        ReadAmplificationNumeratorBytes = readAmplificationNumeratorBytes;
    }

    public uint ObjectId { get; }

    public ReadAmplificationBaseBudgetPolicyObjectKind Kind { get; }

    public bool IsADependent { get; }

    public int PostSaveBasePayloadBytes { get; }

    public long SourceHeadReconstructionObjectPayloadBytes { get; }

    public int? DeltaPayloadBytes { get; }

    public long? ReadAmplificationNumeratorBytes { get; }
}
