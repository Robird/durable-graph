using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>
/// Turns the caller's complete frozen post-live contents into one appendable
/// Revision. Does not infer reachability, authenticate typed bodies, or publish.
/// </summary>
internal static class ObjectRevisionPlanner {
    internal static PreparedObjectRevision PrepareRevision(
        StateRevisionStore store,
        FrameAddress? parentRevisionAddress,
        IEnumerable<PreparedObject> objects,
        ReadAmplificationBaseBudgetParameters parameters,
        bool independentSnapshot = false) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(objects);
        if (independentSnapshot && parentRevisionAddress is null) {
            throw new ArgumentException("An independent snapshot requires a State baseline.", nameof(parentRevisionAddress));
        }
        PreparedObject[] rows = objects.ToArray();
        Dictionary<ObjectId, PreparedObject> byId = [];
        foreach (PreparedObject row in rows) {
            if (row is null) {
                throw new ArgumentException("Prepared objects cannot contain null rows.", nameof(objects));
            }
            if (!byId.TryAdd(row.ObjectId, row)) {
                throw new ArgumentException("ObjectIds must be unique.", nameof(objects));
            }
        }
        Array.Sort(rows, static (left, right) => left.ObjectId.CompareTo(right.ObjectId));

        IReadOnlyDictionary<ObjectId, FrameAddress> parentHeads = parentRevisionAddress is { } parent
            ? store.ReadLiveObjectHeadMap(parent).ToDictionary(static pair => new ObjectId(pair.Key), static pair => pair.Value)
            : new Dictionary<ObjectId, FrameAddress>();
        foreach (PreparedObject row in rows) {
            bool exists = parentHeads.TryGetValue(row.ObjectId, out FrameAddress head);
            if (row.ChangeKind == ObjectSaveChangeKind.Insert) {
                if (exists) {
                    throw new ArgumentException($"New object {row.ObjectId} already exists in Parent.", nameof(objects));
                }
            }
            else if (!exists || row.PriorAddress != head) {
                throw new ArgumentException(
                    $"Object {row.ObjectId} must claim the object head selected by the exact Parent Revision.",
                    nameof(objects));
            }
        }

        ObjectSaveEstimate[] estimates = new ObjectSaveEstimate[rows.Length];
        for (int index = 0; index < rows.Length; index++) {
            PreparedObject row = rows[index];
            long? deltaBytes = row.ChangeKind == ObjectSaveChangeKind.Update
                ? ObjectVersionPayloadSize.EstimateDeltaPayloadBytesUpperBound(row.DeltaBody!.Body.Length, row.PriorAddress!.Value)
                : null;
            // TODO(DB-029): Measure repeated object-chain reads before adding batch/cache support.
            long? reconstructionBytes = row.ChangeKind is ObjectSaveChangeKind.Update or ObjectSaveChangeKind.NoChange
                ? store.ReadObjectVersionChain(parentRevisionAddress!.Value, row.ObjectId.Value).ReconstructionPayloadBytes
                : null;
            estimates[index] = new(row.ObjectId, row.ChangeKind,
                ObjectVersionPayloadSize.GetBasePayloadBytes(row.EncodedBaseBody.Body.Length), deltaBytes, reconstructionBytes);
        }

        ObjectRepresentationPlan plan = ReadAmplificationBaseBudgetPolicy.Plan(estimates, parameters);
        List<ObjectVersionRecord> records = [];
        foreach (ObjectWriteDecision decision in plan.Writes) {
            PreparedObject row = byId[decision.ObjectId];
            records.Add(decision.Mode == ObjectRepresentationMode.Base
                ? ObjectVersionRecord.CreateBase(row.ObjectId.Value, row.EncodedBaseBody.Body)
                : ObjectVersionRecord.CreateDelta(row.ObjectId.Value, row.PriorAddress!.Value, row.DeltaBody!.Body));
        }

        StateRevision revision;
        if (independentSnapshot) {
            // Membership is the candidate closure, while each actual policy-selected write
            // remains local (including an optional Base for an unchanged object).
            HashSet<uint> localIds = records.Select(static record => record.ObjectId).ToHashSet();
            revision = StateRevision.CreateObjectHeadMapBase(parentRevisionAddress, records,
                rows.Where(row => !localIds.Contains(row.ObjectId.Value))
                    .Select(row => new KeyValuePair<uint, FrameAddress>(row.ObjectId.Value, parentHeads[row.ObjectId])));
        } else {
            revision = parentRevisionAddress is { } exactParent
                ? StateRevision.CreateObjectHeadMapDelta(exactParent, records, parentHeads.Keys.Where(id => !byId.ContainsKey(id)).Select(static id => id.Value))
                : StateRevision.CreateObjectHeadMapBase(null, records, []);
        }
        return new(revision, estimates, plan);
    }
}
