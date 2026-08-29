namespace Atelia.TwoLegRotationProbe.Planning;

internal readonly record struct UpdateWriteDecision(
    uint ObjectId,
    UpdateWriteMode Mode);
