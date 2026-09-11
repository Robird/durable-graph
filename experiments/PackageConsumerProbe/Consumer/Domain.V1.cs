using Atelia.DurableGraph;

namespace PackageConsumerProbe;

[DurableType("probe.package-character", 1)]
internal sealed partial class Character : IDurableObject {
    [DurableField(1)]
    private int _score;

    public static string ExerciseGeneratedState() {
        __DurableState.V1 state = new(7);
        return state.Segment0Field1.ToString();
    }
}
