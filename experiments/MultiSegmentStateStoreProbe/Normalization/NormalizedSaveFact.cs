using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Normalization;

internal sealed record SourceObjectFact(
    uint ObjectId,
    LogicalObjectState State,
    AbsoluteFrameAddress HeadAddress,
    IReadOnlyList<AbsoluteFrameAddress> ReconstructionPath);

internal abstract record NormalizedSaveFact(uint ObjectId);

internal sealed record NormalizedInsertFact(
    uint ObjectId,
    LogicalObjectState ResultState) : NormalizedSaveFact(ObjectId);

internal sealed record NormalizedUpdateFact(
    SourceObjectFact Source,
    LogicalObjectState ResultState,
    int DeltaPayloadBytes) : NormalizedSaveFact(Source.ObjectId);

internal sealed record NormalizedRemoveFact(
    SourceObjectFact Source) : NormalizedSaveFact(Source.ObjectId);

internal sealed record NormalizedNoChangeFact(
    SourceObjectFact Source) : NormalizedSaveFact(Source.ObjectId);
