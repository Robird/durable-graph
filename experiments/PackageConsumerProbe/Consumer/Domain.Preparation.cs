using Atelia.DurableGraph;

namespace PackageConsumerProbe;

public sealed partial class Character {
    private static void ExerciseCapturePreparation() {
        CaptureSession session = new();
        Character character = new(true, -17, 42, 8);
        PreparationLabel label = new();
        using var first = session.BeginCapture();
        __DurableBinaryBody.AddRoot(first, character);
        PreparationLabel.__DurableBinaryBody.AddRoot(first, label);
        // Repeated registration must reuse the generated static preparation binding.
        __DurableBinaryBody.AddRoot(first, character);
        CapturedGraph firstGraph = first.Seal();
        character._total = 43;
        label.Change();
        PreparedCapturedGraph initial = session.Prepare(firstGraph);
        PreparedCapturedGraph repeated = session.Prepare(firstGraph);
        string[] initialBodies = ["012154", "0304", "0341", "00"];
        RequireBodies(initial, initialBodies);
        RequireBodies(repeated, initialBodies);
        if (session.Current is not null || initial.Previous is not null ||
            !ReferenceEquals(firstGraph, initial.Candidate) ||
            initial.Objects.Any(row => row.Previous is not null || row.DeltaContent is not null)) {
            throw new InvalidOperationException("Initial preparation changed session state or fabricated a prior.");
        }
        session.Accept(firstGraph);

        using var second = session.BeginCapture();
        __DurableBinaryBody.AddRoot(second, character);
        PreparationLabel.__DurableBinaryBody.AddRoot(second, label);
        CapturedGraph secondGraph = second.Seal();
        PreparedCapturedGraph changed = session.Prepare(secondGraph);
        RequireBodies(changed, ["012156", "0504", "00", "0341"]);
        if (!ReferenceEquals(firstGraph, changed.Previous) ||
            !changed.Objects.Select(row => row.Current.Id).SequenceEqual(new uint[] { 1, 2, 4, 5 }) ||
            changed.Objects[0].DeltaContent is not { HasChanges: true } ownerDelta ||
            !ownerDelta.Payload.SequenceEqual(new byte[] { 0x04, 0x56 }) ||
            changed.Objects[1].DeltaContent is not { HasChanges: true } labelDelta ||
            !labelDelta.Payload.SequenceEqual(new byte[] { 0x01, 0x05 }) ||
            changed.Objects[2].Previous is null || changed.Objects[2].DeltaContent is not null ||
            changed.Objects[3].Previous is not null || changed.Objects[3].DeltaContent is not null) {
            throw new InvalidOperationException("Packaged heterogeneous preparation lost Delta or string identity semantics.");
        }
        session.Accept(secondGraph);

        using var third = session.BeginCapture();
        __DurableBinaryBody.AddRoot(third, character);
        PreparationLabel.__DurableBinaryBody.AddRoot(third, label);
        CapturedGraph thirdGraph = third.Seal();
        PreparedCapturedGraph unchanged = session.Prepare(thirdGraph);
        RequireBodies(unchanged, ["012156", "0504", "00", "0341"]);
        foreach (var row in unchanged.Objects) {
            if (row.Previous is null || (row.Current.Kind == CapturedObjectKind.Durable &&
                (row.DeltaContent is not { HasChanges: false } delta ||
                    !delta.Payload.SequenceEqual(new byte[] { 0x00 })))) {
                throw new InvalidOperationException("Unchanged generated bodies must retain their zero bitmap.");
            }
        }
        session.Discard(thirdGraph);
        RequireBodies(unchanged, ["012156", "0504", "00", "0341"]);
        if (!ReferenceEquals(session.Current, secondGraph)) {
            throw new InvalidOperationException("Preparing or discarding installed the candidate.");
        }
    }

    private static void RequireBodies(PreparedCapturedGraph prepared, string[] expected) {
        if (!prepared.Objects.Select(row => Convert.ToHexString(row.BaseContent.Payload)).SequenceEqual(expected)) {
            throw new InvalidOperationException("Unified preparation changed independently specified Base bytes.");
        }
    }
}

[DurableType("package.preparation-label", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public sealed partial class PreparationLabel : DurableBase {
    [DurableField(1)] private string _name = new(new[] { 'A' });
    [DurableField(2)] private string _empty = string.Empty;

    internal void Change() { _name = new string(new[] { 'A' }); }
}
