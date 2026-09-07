using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.Tests;

public sealed class LoadedCaptureIdentityTests {
    private sealed class World : DurableBase {
        internal string? Text;
    }

    private readonly record struct State(uint Text);
    private static readonly DurableSchema Schema = new("loaded.identity", 1, new DurableFieldInfo(1, TypeTag.String));
    private static readonly CapturedStatePreparation<State> Preparation = new(Schema,
        static (in State state) => new PreparedBase([(byte)state.Text]),
        static (in State prior, in State current) => new PreparedDelta(prior != current, [(byte)current.Text]));

    [Fact]
    public void ImportedIdentityKeepsOriginalBaselineSlotsAndDoesNotFabricateCurrent() {
        World world = new() { Text = string.Empty };
        Dictionary<object, uint> map = new(ReferenceEqualityComparer.Instance) { [world] = 1, [string.Empty] = 3 };
        CaptureSession session = new(10, map);
        map.Clear(); // The caller's map cannot alter the imported identity set.
        Dictionary<uint, CapturedObject> baseline = new() {
            [1] = new(1, Schema, new State(9), Preparation),
            [3] = new(3, string.Empty),
            [9] = new(9, string.Empty),
        };
        using CaptureContext context = session.BeginCapture();
        Assert.Equal(1u, context.AddRoot(world, Schema,
            static (value, capture) => new State(capture.CaptureString(value.Text)), Preparation));
        CapturedGraph candidate = context.Seal();
        IReadOnlyList<PreparedCapturedObject> prepared = session.PrepareAgainst(candidate, baseline);
        Assert.Null(session.Current);
        Assert.Equal<uint>([1, 3], candidate.Objects.Select(row => row.Id));
        Assert.Equal(9u, baseline[1].GetState<State>().Text);
        Assert.True(prepared[0].DeltaContent!.HasChanges);
        Assert.Equal(3u, prepared[0].Current.GetState<State>().Text);
        session.Discard(candidate);
        Assert.Null(session.Current);
    }

    [Fact]
    public void ExhaustedImportedCursorAllowsKnownIdentitiesAndReleasesFailedCapture() {
        World world = new();
        CaptureSession session = new((ulong)uint.MaxValue + 1,
            new Dictionary<object, uint>(ReferenceEqualityComparer.Instance) { [world] = uint.MaxValue });
        using (CaptureContext context = session.BeginCapture()) {
            Assert.Equal(uint.MaxValue, context.AddRoot(world, Schema,
                static (value, capture) => new State(capture.CaptureString(value.Text)), Preparation));
            CapturedGraph candidate = context.Seal();
            session.Discard(candidate);
        }
        world.Text = "new";
        using (CaptureContext context = session.BeginCapture()) {
            context.AddRoot(world, Schema, static (value, capture) => new State(capture.CaptureString(value.Text)), Preparation);
            Assert.Throws<InvalidOperationException>(() => context.Seal());
        }
        world.Text = null;
        using CaptureContext retry = session.BeginCapture();
        retry.AddRoot(world, Schema, static (value, capture) => new State(capture.CaptureString(value.Text)), Preparation);
        Assert.Single(retry.Seal().Objects);
        Assert.Null(session.Current);
    }
}
