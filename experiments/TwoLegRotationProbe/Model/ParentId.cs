namespace Atelia.TwoLegRotationProbe.Model;

internal readonly record struct ParentId(
    bool IsPreviousFile,
    FrameId FrameId);
