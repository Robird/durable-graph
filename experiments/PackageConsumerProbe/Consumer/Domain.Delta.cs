using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace PackageConsumerProbe;

public sealed partial class Character {
    private static void ExercisePreparedDelta() {
        Character priorSource = new(true, -17, 42, 8);
        Character currentSource = new(true, -18, 43, 9);
        var prior = __DurableBinaryBody.Capture(priorSource);
        var current = __DurableBinaryBody.Capture(currentSource);
        priorSource._total = 1000;
        currentSource._total = 2000;

        // One package reference supplies this public result type transitively.
        PreparedDelta delta = __DurableBinaryBody.PrepareDelta(in prior, in current);
        PreparedDelta unchanged = __DurableBinaryBody.PrepareDelta(in prior, in prior);
        // Base-first layout is bool, int, long; the latter two change.
        // ZigZag(-18) = 35 (0x23), ZigZag(43) = 86 (0x56).
        byte[] deltaGolden = [0x06, 0x23, 0x56];
        if (!delta.HasChanges || delta.Payload.Length != 3 ||
            !delta.Payload.SequenceEqual(deltaGolden) || unchanged.HasChanges ||
            !unchanged.Payload.SequenceEqual(new byte[] { 0x00 })) {
            throw new InvalidOperationException("Packaged fused Delta result disagrees with its golden bytes.");
        }

        byte[] externalCopy = delta.Payload.ToArray();
        externalCopy[0] = 0xFF;
        // Reuse the prepared bytes directly, including after another Prepare and an external mutation.
        for (int attempt = 0; attempt < 2; attempt++) {
            BinaryPayloadReader reader = new(delta.Payload);
            var applied = __DurableBinaryBody.ApplyDeltaV1(ref reader, in prior);
            reader.EnsureFullyConsumed();
            ArrayBufferWriter<byte> buffer = new();
            BinaryPayloadWriter writer = new(buffer);
            __DurableBinaryBody.Write(ref writer, in applied);
            if (!buffer.WrittenSpan.SequenceEqual(new byte[] { 0x01, 0x23, 0x56 }) ||
                __DurableBinaryBody.PrepareDelta(in applied, in current).HasChanges) {
                throw new InvalidOperationException("Packaged Delta did not reconstruct the frozen current DTO.");
            }
        }

        BinaryPayloadReader noChangeReader = new(unchanged.Payload);
        var retained = __DurableBinaryBody.ApplyDeltaV1(ref noChangeReader, in prior);
        noChangeReader.EnsureFullyConsumed();
        if (__DurableBinaryBody.PrepareDelta(in retained, in prior).HasChanges ||
            prior.Segment1Field1 != 42 || current.Segment1Field1 != 43 ||
            !delta.Payload.SequenceEqual(deltaGolden)) {
            throw new InvalidOperationException("Packaged Delta mutated a frozen DTO or owned result.");
        }

        try {
            // Three slots cannot use bitmap bit 3.
            BinaryPayloadReader malformed = new(new byte[] { 0x08 });
            _ = __DurableBinaryBody.ApplyDeltaV1(ref malformed, in prior);
        }
        catch (InvalidDataException) {
            return;
        }
        throw new InvalidOperationException("Packaged Delta accepted out-of-layout bitmap bits.");
    }
}
