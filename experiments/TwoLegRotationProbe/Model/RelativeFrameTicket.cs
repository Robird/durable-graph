namespace Atelia.TwoLegRotationProbe.Model;

/// <summary>
/// </summary>
/// <param name="IsPreviousFile">由<see cref="FileScope"/>解释。</param>
/// <param name="FrameTicket"></param>
internal readonly record struct RelativeFrameTicket(
    bool IsPreviousFile,
    FrameTicket FrameTicket);
