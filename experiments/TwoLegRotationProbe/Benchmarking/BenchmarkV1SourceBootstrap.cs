using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1SourceBootstrap {
    public static BenchmarkV1BootstrappedSource Create(WorkloadTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);

        CreateObject[] initialObjects = trace.Steps[0].Changes
            .Select(change => change as CreateObject ??
                throw new InvalidDataException(
                    "Benchmark v1 trace step 0 must contain only Create changes."))
            .ToArray();
        foreach (CreateObject create in initialObjects) {
            if (create.BasePayloadBytes < 0) {
                throw new InvalidDataException(
                    $"Bootstrap object {create.ObjectId} has a negative Base payload size.");
            }
        }

        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
        };
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        foreach (CreateObject create in initialObjects) {
            previousDictionary.BindSelf(create.ObjectId);
            RevisionCandidateRecordBuilder.AddBase(
                previousBuilder,
                create.ObjectId,
                new LogicalObjectState(
                    create.BasePayloadBytes,
                    LogicalVersionOrdinal: 1));
        }

        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FileScope currentScope = new(current.FileNumber);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = currentScope.Relativize(previousRevision),
            },
        };
        AbsoluteFrameAddress publishedRevision = AppendExact(
            current,
            currentBuilder);
        ProbeRevisionCursor cursor = new(
            currentScope,
            publishedRevision,
            current.TailOffsetBytes);
        return new BenchmarkV1BootstrappedSource(
            store,
            cursor,
            previousRevision);
    }

    private static AbsoluteFrameAddress AppendExact(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(
                frame,
                file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        if (ticket != estimate.RbfLayout.Ticket) {
            throw new InvalidDataException(
                "Bootstrap append did not preserve the exact provisional layout.");
        }

        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }
}

internal sealed record BenchmarkV1BootstrappedSource(
    RbfFileStore Store,
    ProbeRevisionCursor Cursor,
    AbsoluteFrameAddress PreviousRevisionAddress);
