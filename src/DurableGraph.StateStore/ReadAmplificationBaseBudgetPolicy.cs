namespace Atelia.DurableGraph.StateStore;

/// <summary>
/// Selects object representations from estimates for one frozen, complete post-save live set.
/// The optional Base budget is soft; required writes do not consume it.
/// </summary>
internal static class ReadAmplificationBaseBudgetPolicy {
    internal static ObjectRepresentationPlan Plan(
        IReadOnlyList<ObjectSaveEstimate> objects,
        ReadAmplificationBaseBudgetParameters parameters) {
        ArgumentNullException.ThrowIfNull(objects);
        if (parameters.ReadAmplificationLimit < 1) {
            throw new ArgumentOutOfRangeException(
                nameof(parameters), parameters, "Read amplification limit must be at least one.");
        }

        if (parameters.BaseBudgetPercent is < 1 or > 100) {
            throw new ArgumentOutOfRangeException(
                nameof(parameters), parameters, "Base budget percent must be between one and one hundred.");
        }

        HashSet<uint> objectIds = new();
        List<ObjectWriteDecision> writes = new();
        List<Candidate> candidates = new();
        long graphBaseBytes = 0;

        foreach (ObjectSaveEstimate estimate in objects) {
            ValidateEstimate(estimate);
            if (!objectIds.Add(estimate.ObjectId)) {
                throw new ArgumentException("ObjectIds must be unique.", nameof(objects));
            }

            graphBaseBytes = checked(graphBaseBytes + estimate.EstimatedBaseWriteBytes);
            long reconstructionBytes = estimate.ChangeKind == ObjectSaveChangeKind.Update
                ? checked(estimate.CurrentReconstructionBytes!.Value + estimate.EstimatedDeltaWriteBytes!.Value)
                : estimate.CurrentReconstructionBytes.GetValueOrDefault();

            if (estimate.ChangeKind is ObjectSaveChangeKind.Insert or ObjectSaveChangeKind.BaseOnlyUpdate ||
                (estimate.ChangeKind == ObjectSaveChangeKind.Update &&
                    estimate.EstimatedBaseWriteBytes <= estimate.EstimatedDeltaWriteBytes!.Value)) {
                writes.Add(new(estimate.ObjectId, ObjectRepresentationMode.Base));
                continue;
            }

            int writeIndex = -1;
            if (estimate.ChangeKind == ObjectSaveChangeKind.Update) {
                writeIndex = writes.Count;
                writes.Add(new(estimate.ObjectId, ObjectRepresentationMode.Delta));
            }

            // At a zero denominator, 0/0 has no motive and positive/0 always has one.
            if (reconstructionBytes > (Int128)estimate.EstimatedBaseWriteBytes * parameters.ReadAmplificationLimit) {
                candidates.Add(new(
                    estimate.ObjectId, estimate.EstimatedBaseWriteBytes, reconstructionBytes, writeIndex));
            }
        }

        // All rows, including mandatory Base updates, have now passed overflow validation.
        candidates.Sort(CompareCandidates);
        long remainingBudget = (long)((Int128)graphBaseBytes * parameters.BaseBudgetPercent / 100);
        for (int i = 0; i < candidates.Count; i++) {
            Candidate candidate = candidates[i];
            bool exceedsBudget = candidate.BaseWriteBytes > remainingBudget;
            if (exceedsBudget && i != 0) {
                break;
            }

            ObjectWriteDecision decision = new(candidate.ObjectId, ObjectRepresentationMode.Base);
            if (candidate.WriteIndex >= 0) {
                writes[candidate.WriteIndex] = decision;
            }
            else {
                writes.Add(decision);
            }

            // Only the actual first candidate may overshoot, even when earlier costs were zero.
            if (exceedsBudget) {
                break;
            }

            remainingBudget -= candidate.BaseWriteBytes;
        }

        writes.Sort(static (left, right) => left.ObjectId.CompareTo(right.ObjectId));
        return new(writes.ToArray());
    }

    private static void ValidateEstimate(ObjectSaveEstimate estimate) {
        const string parameterName = "objects";
        if (estimate.ObjectId == 0) {
            throw new ArgumentException("ObjectIds must be nonzero.", parameterName);
        }

        if (estimate.EstimatedBaseWriteBytes < 0 ||
            estimate.EstimatedDeltaWriteBytes < 0 ||
            estimate.CurrentReconstructionBytes < 0) {
            throw new ArgumentOutOfRangeException(parameterName, "Byte estimates must be nonnegative.");
        }

        bool validShape;
        switch (estimate.ChangeKind) {
            case ObjectSaveChangeKind.Insert:
            case ObjectSaveChangeKind.BaseOnlyUpdate:
                validShape = estimate.EstimatedDeltaWriteBytes is null && estimate.CurrentReconstructionBytes is null;
                break;
            case ObjectSaveChangeKind.Update:
                validShape = estimate.EstimatedDeltaWriteBytes is not null && estimate.CurrentReconstructionBytes is not null;
                break;
            case ObjectSaveChangeKind.NoChange:
                validShape = estimate.EstimatedDeltaWriteBytes is null && estimate.CurrentReconstructionBytes is not null;
                break;
            default:
                throw new ArgumentOutOfRangeException(parameterName, estimate.ChangeKind, "Unknown object change kind.");
        }

        if (!validShape) {
            throw new ArgumentException("Delta and reconstruction estimates must match the object change kind.", parameterName);
        }
    }

    private static int CompareCandidates(Candidate left, Candidate right) {
        // A zero-Base candidate necessarily has positive reconstruction cost: it ranks as infinity.
        int byAmplification;
        if (left.BaseWriteBytes == 0 || right.BaseWriteBytes == 0) {
            byAmplification = left.BaseWriteBytes == 0
                ? (right.BaseWriteBytes == 0 ? 0 : -1)
                : 1;
        }
        else {
            Int128 leftProduct = (Int128)left.ReconstructionBytes * right.BaseWriteBytes;
            Int128 rightProduct = (Int128)right.ReconstructionBytes * left.BaseWriteBytes;
            byAmplification = rightProduct.CompareTo(leftProduct);
        }

        return byAmplification != 0 ? byAmplification : left.ObjectId.CompareTo(right.ObjectId);
    }

    private readonly record struct Candidate(
        uint ObjectId,
        long BaseWriteBytes,
        long ReconstructionBytes,
        int WriteIndex);
}
