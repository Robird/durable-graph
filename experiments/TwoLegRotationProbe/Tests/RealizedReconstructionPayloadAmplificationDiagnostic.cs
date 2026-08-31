using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Tests;

/// <summary>
/// Classification of the realized accepted-head H/B payload ratio.
/// This diagnostic deliberately does not include a prospective Delta payload.
/// </summary>
internal enum RealizedReconstructionPayloadAmplificationKind {
    Finite,
    ZeroOverZero,
    PositiveOverZeroInfinity,
}

/// <summary>
/// A frozen per-object payload sample from a normalized ParentLive view.
/// </summary>
internal sealed record RealizedReconstructionPayloadAmplificationSample {
    public RealizedReconstructionPayloadAmplificationSample(
        uint objectId,
        long headReconstructionObjectPayloadBytes,
        int basePayloadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(
            headReconstructionObjectPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(basePayloadBytes);

        ObjectId = objectId;
        HeadReconstructionObjectPayloadBytes =
            headReconstructionObjectPayloadBytes;
        BasePayloadBytes = basePayloadBytes;

        if (basePayloadBytes > 0) {
            Kind = RealizedReconstructionPayloadAmplificationKind.Finite;
            EffectiveHeadToBasePayloadAmplification =
                (decimal)headReconstructionObjectPayloadBytes / basePayloadBytes;
        } else if (headReconstructionObjectPayloadBytes == 0) {
            Kind = RealizedReconstructionPayloadAmplificationKind.ZeroOverZero;
            EffectiveHeadToBasePayloadAmplification = 1m;
        } else {
            Kind = RealizedReconstructionPayloadAmplificationKind
                .PositiveOverZeroInfinity;
            EffectiveHeadToBasePayloadAmplification = null;
        }
    }

    public uint ObjectId { get; }

    /// <summary>Raw H: payload bytes in the realized head reconstruction chain.</summary>
    public long HeadReconstructionObjectPayloadBytes { get; }

    /// <summary>Raw B: Base payload bytes in the realized logical state.</summary>
    public int BasePayloadBytes { get; }

    public RealizedReconstructionPayloadAmplificationKind Kind { get; }

    /// <summary>
    /// H/B for finite samples, 1 for 0/0, and null for positive/0 infinity.
    /// </summary>
    public decimal? EffectiveHeadToBasePayloadAmplification { get; }
}

/// <summary>
/// Test-local, read-only checkpoint projection over every ParentLive object.
/// It is an observation only, not a candidate or sizing authority.
/// </summary>
internal sealed class RealizedReconstructionPayloadAmplificationDiagnostic {
    private readonly ReadOnlyCollection<
        RealizedReconstructionPayloadAmplificationSample> _samples;

    private RealizedReconstructionPayloadAmplificationDiagnostic(
        RealizedReconstructionPayloadAmplificationSample[] samples) {
        _samples = Array.AsReadOnly(samples);
    }

    public IReadOnlyList<RealizedReconstructionPayloadAmplificationSample> Samples =>
        _samples;

    public static RealizedReconstructionPayloadAmplificationDiagnostic Capture(
        NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);

        RealizedReconstructionPayloadAmplificationSample[] samples = facts
            .ParentLive
            .Values
            .OrderBy(static source => source.ObjectId)
            .Select(static source =>
                new RealizedReconstructionPayloadAmplificationSample(
                    source.ObjectId,
                    source.HeadReconstructionObjectPayloadBytes,
                    source.State.BasePayloadBytes))
            .ToArray();
        return new RealizedReconstructionPayloadAmplificationDiagnostic(samples);
    }
}
