using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

internal abstract class NormalizedSaveFact {
    protected NormalizedSaveFact(uint objectId) {
        ObjectId = objectId;
    }

    public uint ObjectId { get; }
}

internal sealed class NormalizedInsertFact : NormalizedSaveFact {
    internal NormalizedInsertFact(
        uint objectId,
        LogicalObjectState resultState)
        : base(objectId) {
        ResultState = resultState;
    }

    public LogicalObjectState ResultState { get; }
}

internal sealed class NormalizedUpdateFact : NormalizedSaveFact {
    internal NormalizedUpdateFact(
        SourceObjectFact source,
        LogicalObjectState resultState,
        int deltaPayloadBytes)
        : base((source ?? throw new ArgumentNullException(nameof(source))).ObjectId) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaPayloadBytes);
        Source = source;
        ResultState = resultState;
        DeltaPayloadBytes = deltaPayloadBytes;
    }

    public SourceObjectFact Source { get; }

    public LogicalObjectState ResultState { get; }

    public int DeltaPayloadBytes { get; }
}

internal sealed class NormalizedRemoveFact : NormalizedSaveFact {
    internal NormalizedRemoveFact(SourceObjectFact source)
        : base((source ?? throw new ArgumentNullException(nameof(source))).ObjectId) {
        Source = source;
    }

    public SourceObjectFact Source { get; }
}

internal sealed class NormalizedNoChangeFact : NormalizedSaveFact {
    internal NormalizedNoChangeFact(SourceObjectFact source)
        : base((source ?? throw new ArgumentNullException(nameof(source))).ObjectId) {
        Source = source;
    }

    public SourceObjectFact Source { get; }
}
