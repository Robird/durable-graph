using System.Collections;
using System.Runtime.CompilerServices;

namespace Atelia.DurableGraph.Tests;

public sealed class ReferenceCaptureSessionTests {
    private static readonly DurableSchema Schema = new("capture.test", 1,
        new DurableFieldInfo(1, TypeTag.Int32), new DurableFieldInfo(2, TypeTag.String),
        new DurableFieldInfo(3, TypeTag.String));

    private class Domain : DurableBase {
        public int Value;
        public string? First;
        public string? Second;

        // Domain equality must not participate in identity allocation either.
        public override bool Equals(object? obj) => obj is Domain;
        public override int GetHashCode() => 0;
    }

    private sealed class Derived : Domain { }
    private struct State {
        public int Value;
        public uint First;
        public uint Second;
    }

    private static State Capture(Domain source, CaptureContext context) => new() {
        Value = source.Value,
        First = context.CaptureString(source.First),
        Second = context.CaptureString(source.Second),
    };

    private static uint Add(CaptureContext context, Domain? source) =>
        context.AddRoot<Domain, State>(source, Schema, Capture);

    private static CapturedGraph CaptureGraph(CaptureSession session, params Domain?[] roots) {
        // The caller must resolve the sealed result; don't Dispose this live candidate here.
        CaptureContext context = session.BeginCapture();
        foreach (Domain? root in roots) {
            Add(context, root);
        }
        return context.Seal();
    }

    [Fact]
    public void ReferenceCapturePreservesRootAndStringReferenceIdentityAndDiscoveryOrder() {
        string shared = new(['A', 'd', 'a']);
        string equal = new(['A', 'd', 'a']);
        Assert.NotSame(shared, equal);
        Domain first = new() { Value = 7, First = shared, Second = shared };
        Domain second = new() { Value = 8, First = equal, Second = shared };
        CaptureSession session = new();
        CapturedGraph graph = CaptureGraph(session, first, null, second, first);

        Assert.Equal<uint>([1, 0, 2, 1], graph.RootIds);
        Assert.Equal<uint>([1, 2, 3, 4], graph.Objects.Select(item => item.Id));
        State firstState = graph.Objects[0].GetState<State>();
        State secondState = graph.Objects[1].GetState<State>();
        Assert.Equal(7, firstState.Value);
        Assert.Equal(3u, firstState.First);
        Assert.Equal(firstState.First, firstState.Second);
        Assert.Equal(4u, secondState.First);
        Assert.Equal(firstState.First, secondState.Second);
        Assert.Same(shared, graph.Objects[2].StringContent);
        Assert.Same(equal, graph.Objects[3].StringContent);
        Assert.Same(Schema, graph.Objects[0].Schema);
        Assert.Null(graph.Objects[2].Schema);
        session.Accept(graph);
        Assert.Same(graph, session.Current);
    }

    [Fact]
    public void ReferenceCaptureSupportsNullEmptyAndUnpairedSurrogateWithoutContentDecoding() {
        string surrogate = new(['\uD800']);
        CaptureSession session = new();
        CapturedGraph graph = CaptureGraph(session,
            new Domain { First = null, Second = string.Empty },
            new Domain { First = surrogate, Second = string.Empty });
        Assert.Equal(4, graph.Objects.Count);
        Assert.Equal(0u, graph.Objects[0].GetState<State>().First);
        Assert.Equal(3u, graph.Objects[0].GetState<State>().Second);
        Assert.Equal(3u, graph.Objects[1].GetState<State>().Second);
        Assert.Same(string.Empty, graph.Objects[2].StringContent);
        Assert.Same(surrogate, graph.Objects[3].StringContent);
        session.Discard(graph);
    }

    [Fact]
    public void ReferenceCaptureSealsCopiesAndAcceptDoesNotRecapture() {
        CaptureSession session = new();
        Domain source = new() { Value = 1, First = new(['o', 'l', 'd']) };
        int calls = 0;
        State Counted(Domain domain, CaptureContext context) {
            calls++;
            return Capture(domain, context);
        }
        using CaptureContext context = session.BeginCapture();
        context.AddRoot<Domain, State>(source, Schema, Counted);
        context.AddRoot<Domain, State>(source, Schema, Counted);
        Assert.Equal(0, calls);
        source.Value = 2; // Copy happens at Seal, after root registration.
        CapturedGraph graph = context.Seal();
        Assert.Equal(1, calls);
        source.Value = 3;
        source.First = "new";
        State copy = graph.Objects[0].GetState<State>();
        copy.Value = 99;
        Assert.Equal(2, graph.Objects[0].GetState<State>().Value);
        Assert.Equal("old", graph.Objects[1].StringContent);
        session.Accept(graph);
        Assert.Equal(1, calls);
        Assert.Same(graph, session.Current);
        Assert.Equal(2, session.Current!.Objects[0].GetState<State>().Value);
    }

    [Fact]
    public void ReferenceCaptureSealedCollectionsAndKindAccessCannotExposeMutableState() {
        CaptureSession session = new();
        CapturedGraph graph = CaptureGraph(session, new Domain { First = "value" });
        Assert.False(graph.RootIds is uint[]);
        Assert.False(graph.Objects is CapturedObject[]);
        // ReadOnlyCollection over an array exposes that array through ICollection.SyncRoot.
        // A sealed result must not expose this mutation path either.
        Assert.False(graph.RootIds is ICollection);
        Assert.False(graph.Objects is ICollection);
        Assert.False(graph.RootIds is IList<uint>);
        Assert.False(graph.Objects is IList<CapturedObject>);
        Assert.Throws<InvalidOperationException>(() => graph.Objects[0].GetState<long>());
        Assert.Throws<InvalidOperationException>(() => graph.Objects[1].GetState<State>());
        Assert.Throws<InvalidOperationException>(() => graph.Objects[0].StringContent);
        session.Discard(graph);
        Assert.Equal("value", graph.Objects[1].StringContent);
        Assert.Equal(2u, graph.Objects[0].GetState<State>().First);
    }

    [Fact]
    public void ReferenceCaptureLiveBindingsRemainStableAndRetirementEndsTheirLifetime() {
        CaptureSession session = new();
        string shared = new(['s']);
        Domain retired = new() { First = shared };
        Domain survivor = new() { First = shared };
        CapturedGraph old = CaptureGraph(session, retired, survivor);
        session.Accept(old);
        CapturedGraph next = CaptureGraph(session, survivor);
        Assert.Equal<uint>([2], next.RootIds);
        Assert.Equal<uint>([2, 3], next.Objects.Select(item => item.Id));
        session.Accept(next);

        CapturedGraph reintroduced = CaptureGraph(session, retired, survivor);
        Assert.Equal<uint>([4, 2], reintroduced.RootIds);
        Assert.Equal(3u, reintroduced.Objects.Single(item => item.Id == 4).GetState<State>().First);
        session.Accept(reintroduced);

        CapturedGraph empty = CaptureGraph(session);
        session.Accept(empty);
        Assert.Empty(empty.Objects);
        CapturedGraph returned = CaptureGraph(session, retired);
        Assert.Equal<uint>([5], returned.RootIds);
        Assert.Equal(6u, returned.Objects[0].GetState<State>().First);
        session.Accept(returned);
        Assert.Equal<uint>([1, 2], old.RootIds);
        Assert.Same(shared, old.Objects[2].StringContent);
    }

    [Fact]
    public void ReferenceCaptureDiscardBurnsNewIdsWithoutChangingParentOrSurvivingBindings() {
        CaptureSession session = new();
        Domain survivor = new() { Value = 4, First = new(['s']) };
        CapturedGraph parent = CaptureGraph(session, survivor);
        session.Accept(parent);
        Domain pending = new() { First = new(['p']) };
        CapturedGraph discarded = CaptureGraph(session, survivor, pending);
        Assert.Equal<uint>([1, 3], discarded.RootIds);
        session.Discard(discarded);
        Assert.Same(parent, session.Current);
        CapturedGraph retry = CaptureGraph(session, pending, survivor);
        Assert.Equal<uint>([5, 1], retry.RootIds);
        Assert.Equal<uint>([1, 2, 5, 6], retry.Objects.Select(item => item.Id));
        session.Accept(retry);
    }

    [Fact]
    public void ReferenceCaptureFailureAfterStringAllocationAbortsAndBurnsAllNewIds() {
        CaptureSession session = new();
        Domain survivor = new() { Value = 4 };
        CapturedGraph parent = CaptureGraph(session, survivor);
        session.Accept(parent);
        Domain pending = new() { First = new(['p']) };
        using CaptureContext failed = session.BeginCapture();
        failed.AddRoot<Domain, State>(pending, Schema, static (value, context) => {
            context.CaptureString(value.First);
            throw new FormatException("capture failed");
        });
        Assert.Throws<FormatException>(() => failed.Seal());
        Assert.Same(parent, session.Current);
        Assert.Throws<InvalidOperationException>(() => failed.Seal());
        CapturedGraph retry = CaptureGraph(session, pending, survivor);
        Assert.Equal<uint>([4, 1], retry.RootIds);
        Assert.Equal<uint>([1, 4, 5], retry.Objects.Select(item => item.Id));
        session.Accept(retry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceCaptureDisposeAbandonsBuildingOrSealedCandidate(bool seal) {
        CaptureSession session = new();
        Domain source = new() { First = "text" };
        CaptureContext context = session.BeginCapture();
        Add(context, source);
        CapturedGraph? abandoned = seal ? context.Seal() : null;
        context.Dispose();
        context.Dispose();
        Assert.Null(session.Current);
        if (abandoned is not null) {
            Assert.Throws<InvalidOperationException>(() => session.Accept(abandoned));
            Assert.Equal("text", abandoned.Objects[1].StringContent);
        }
        CapturedGraph retry = CaptureGraph(session, source);
        Assert.Equal(seal ? 3u : 2u, retry.RootIds[0]);
        session.Accept(retry);
        context.Dispose();
        Assert.Same(retry, session.Current);
    }

    [Fact]
    public void ReferenceCaptureRejectsConcurrentForeignConsumedAndStaleCandidates() {
        CaptureSession session = new();
        CaptureSession other = new();
        using CaptureContext context = session.BeginCapture();
        Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
        CapturedGraph graph = context.Seal();
        Assert.Throws<InvalidOperationException>(() => session.BeginCapture());
        Assert.Throws<InvalidOperationException>(() => other.Accept(graph));
        Assert.Throws<InvalidOperationException>(() => other.Discard(graph));
        Assert.Throws<ArgumentNullException>(() => session.Accept(null!));
        Assert.Throws<InvalidOperationException>(() => context.Seal());
        Assert.Throws<InvalidOperationException>(() => Add(context, new Domain()));
        Assert.Throws<InvalidOperationException>(() => context.CaptureString("late"));
        session.Accept(graph); // Invalid writes to a sealed context don't corrupt its result.
        Assert.Throws<InvalidOperationException>(() => session.Accept(graph));
        Assert.Throws<InvalidOperationException>(() => session.Discard(graph));
        CapturedGraph fresh = CaptureGraph(session);
        Assert.Throws<InvalidOperationException>(() => session.Accept(graph));
        session.Discard(fresh);
        Assert.Throws<InvalidOperationException>(() => session.Accept(fresh));
        Assert.Same(graph, session.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReferenceCaptureInconsistentOrInexactRootBindingPoisonsBuild(int invalidBinding) {
        CaptureSession session = new();
        Domain source = invalidBinding == 3 ? new Derived() : new Domain();
        using CaptureContext context = session.BeginCapture();
        if (invalidBinding == 3) {
            // Check exact type even if this instance has already been registered correctly.
            context.AddRoot<Derived, State>((Derived)source, Schema,
                static (value, shared) => Capture(value, shared));
        } else {
            Add(context, source);
        }
        Assert.Throws<ArgumentException>(() => {
            switch (invalidBinding) {
                case 0:
                    context.AddRoot<Domain, State>(source, new DurableSchema("different", 1), Capture);
                    break;
                case 1:
                    context.AddRoot<Domain, long>(source, Schema, static (_, _) => 1L);
                    break;
                case 2:
                    context.AddRoot<Domain, State>(source, Schema, static (_, _) => default);
                    break;
                default:
                    Add(context, source);
                    break;
            }
        });
        Assert.Throws<InvalidOperationException>(() => context.Seal());
        CapturedGraph retry = CaptureGraph(session, new Domain());
        Assert.Equal(2u, retry.RootIds[0]);
        session.Accept(retry);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReferenceCaptureCaughtReentrantFailureCannotProduceAnAcceptableGraph(int operation) {
        CaptureSession session = new();
        using CaptureContext context = session.BeginCapture();
        context.AddRoot<Domain, State>(new Domain(), Schema, (source, shared) => {
            shared.CaptureString("allocated");
            if (operation == 2) {
                shared.Dispose();
            } else {
                Assert.Throws<InvalidOperationException>(() => {
                    if (operation == 0) {
                        Add(shared, source);
                    } else {
                        shared.Seal();
                    }
                });
            }
            return default;
        });
        Assert.Throws<InvalidOperationException>(() => context.Seal());
        CapturedGraph retry = CaptureGraph(session, new Domain());
        Assert.Equal(3u, retry.RootIds[0]);
        session.Accept(retry);
    }

    [Fact]
    public void ReferenceCaptureStringOutsideCallbackAndInvalidArgumentsTerminateBuild() {
        CaptureSession session = new();
        using CaptureContext first = session.BeginCapture();
        Add(first, new Domain());
        Assert.Throws<InvalidOperationException>(() => first.CaptureString(null));
        using CaptureContext second = session.BeginCapture();
        Assert.Throws<ArgumentNullException>(() => second.AddRoot<Domain, State>(null, null!, Capture));
        using CaptureContext third = session.BeginCapture();
        Assert.Throws<ArgumentNullException>(() => third.AddRoot<Domain, State>(null, Schema, null!));
        CapturedGraph retry = CaptureGraph(session, new Domain());
        Assert.Equal(2u, retry.RootIds[0]);
        session.Accept(retry);
    }

    [Fact]
    public void ReferenceCaptureFinalUintIdIsValidAndExhaustionDoesNotWrapOrLoseParent() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSession(0));
        CaptureSession session = new(uint.MaxValue - 1);
        Domain root = new() { First = new(['l', 'a', 's', 't']) };
        CapturedGraph parent = CaptureGraph(session, root);
        Assert.Equal<uint>([uint.MaxValue - 1, uint.MaxValue], parent.Objects.Select(item => item.Id));
        session.Accept(parent);
        CapturedGraph stable = CaptureGraph(session, root);
        session.Accept(stable); // Existing identities remain usable after exhaustion.
        using CaptureContext failing = session.BeginCapture();
        Assert.Throws<InvalidOperationException>(() => Add(failing, new Domain()));
        Assert.Same(stable, session.Current);
        CapturedGraph unchanged = CaptureGraph(session, root);
        Assert.Equal<uint>([uint.MaxValue - 1], unchanged.RootIds);
        session.Discard(unchanged);

        CaptureSession failedLast = new(uint.MaxValue);
        using CaptureContext failed = failedLast.BeginCapture();
        Add(failed, new Domain { First = "cannot allocate string" });
        Assert.Throws<InvalidOperationException>(() => failed.Seal());
        using CaptureContext retry = failedLast.BeginCapture();
        Assert.Throws<InvalidOperationException>(() => Add(retry, new Domain()));
        Assert.Null(failedLast.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReferenceCaptureResolvedContextAndRetainedGraphReleaseSourcesAndCallbacks(int resolution) {
        CaptureSession session = new();
        var witness = CreateReleasedWitness(session, resolution);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(witness.Source.IsAlive);
        Assert.False(witness.Callback.IsAlive);
        GC.KeepAlive(witness.Context);
        GC.KeepAlive(witness.Graph);
        GC.KeepAlive(session);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Source, WeakReference Callback, CaptureContext Context, CapturedGraph? Graph)
        CreateReleasedWitness(CaptureSession session, int resolution) {
        Domain source = new() { Value = 9 };
        object callbackLifetime = new();
        CaptureContext context = session.BeginCapture();
        context.AddRoot<Domain, State>(source, Schema, (value, shared) => {
            GC.KeepAlive(callbackLifetime);
            if (resolution == 3) {
                throw new FormatException();
            }
            return Capture(value, shared);
        });
        CapturedGraph? graph = null;
        if (resolution == 3) {
            Assert.Throws<FormatException>(() => context.Seal());
        } else {
            graph = context.Seal();
            if (resolution == 0) {
                session.Accept(graph);
                // A later empty accepted graph retires the domain binding.
                session.Accept(CaptureGraph(session));
            } else if (resolution == 1) {
                session.Discard(graph);
            } else {
                context.Dispose();
            }
        }
        return (new(source), new(callbackLifetime), context, graph);
    }
}
