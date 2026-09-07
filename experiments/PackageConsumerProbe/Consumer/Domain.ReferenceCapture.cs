using System.Buffers;
using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore.Serialization;

namespace PackageConsumerProbe;

[DurableType("package.capture-base", 1)]
public abstract partial class CaptureBase : DurableBase {
    [DurableField(1)] private string _name;

    protected CaptureBase(string name) { _name = name; }
}

[DurableType("package.capture-hero", 1)]
public sealed partial class CaptureHero : CaptureBase {
    [DurableField(1)] private string _alias;
    [DurableField(2)] private int _score = 7;

    public CaptureHero(string name) : base(name) { _alias = name; }
    public void ChangeAlias(string alias) { _alias = alias; _score = 90; }
}

[DurableType("package.capture-item", 1)]
public sealed partial class CaptureItem : DurableBase {
    [DurableField(1)] private string _shared;
    [DurableField(2)] private string _equalButDistinct;
    [DurableField(3)] private string? _optional = null;
    [DurableField(4)] private string _surrogate = "\uD800";
    [DurableField(5)] private string _empty = string.Empty;

    public CaptureItem(string shared, string distinct) { _shared = shared; _equalButDistinct = distinct; }
}

internal static class ReferenceCaptureExercise {
    internal static void Run() {
        string shared = new(['A', 'd', 'a']);
        string equal = new(['A', 'd', 'a']);
        Require(!ReferenceEquals(shared, equal), "Fixture must have distinct equal strings.");
        CaptureHero hero = new(shared);
        CaptureItem item = new(shared, equal);
        CaptureSession session = new();
        CapturedGraph first;
        using (var capture = session.BeginCapture()) {
            Require(CaptureHero.__DurableBinaryBody.AddRoot(capture, hero) == 1, "First root ID.");
            Require(CaptureItem.__DurableBinaryBody.AddRoot(capture, item) == 2, "Second root ID.");
            CaptureHero.__DurableBinaryBody.AddRoot(capture, hero);
            CaptureHero.__DurableBinaryBody.AddRoot(capture, null);
            first = capture.Seal();
            Require(first.RootIds.SequenceEqual(new uint[] { 1, 2, 1, 0 }), "Root identity/order.");
            var heroState = first.Objects.Single(entry => entry.Id == 1)
                .GetState<CaptureHero.__DurableBinaryBody.V1>();
            var itemState = first.Objects.Single(entry => entry.Id == 2)
                .GetState<CaptureItem.__DurableBinaryBody.V1>();
            Require(heroState.Segment0Field1 == 3 && heroState.Segment1Field1 == 3 &&
                itemState.Segment0Field1 == 3 && itemState.Segment0Field2 == 4 &&
                itemState.Segment0Field3 == 0, "Shared and distinct string IDs.");
            Require(first.Objects.Count == 6 && first.Objects.Count(entry => entry.Kind == CapturedObjectKind.String) == 4,
                "Closed object list.");
            Require(ReferenceEquals(first.Objects.Single(entry => entry.Id == 3).StringContent, shared) &&
                ReferenceEquals(first.Objects.Single(entry => entry.Id == 4).StringContent, equal) &&
                first.Objects.Single(entry => entry.Id == 5).StringContent == "\uD800" &&
                first.Objects.Single(entry => entry.Id == 6).StringContent.Length == 0, "String content identity.");
            Require(ReferenceEquals(first.Objects.Single(entry => entry.Id == 1).Schema, CaptureHero.Schema),
                "Exact root schema.");

            ArrayBufferWriter<byte> bytes = new();
            BinaryPayloadWriter writer = new(bytes);
            CaptureHero.__DurableBinaryBody.Write(ref writer, in heroState);
            Require(Convert.ToHexString(bytes.WrittenSpan) == "03030E", "Static reference ID golden.");
            BinaryPayloadReader reader = new(bytes.WrittenSpan);
            var decoded = CaptureHero.__DurableBinaryBody.ReadV1(ref reader);
            reader.EnsureFullyConsumed();
            Require(decoded.Segment0Field1 == 3 && decoded.Segment1Field1 == 3 && decoded.Segment1Field2 == 7,
                "Typed ID body round trip.");

            hero.ChangeAlias(new string(['n', 'e', 'w']));
            session.Accept(first);
            Require(ReferenceEquals(first, session.Current) &&
                first.Objects.Single(entry => entry.Id == 1).GetState<CaptureHero.__DurableBinaryBody.V1>().Segment1Field2 == 7,
                "Accept must install the captured state.");
        }

        uint discardedId;
        using (var capture = session.BeginCapture()) {
            CaptureHero.__DurableBinaryBody.AddRoot(capture, hero);
            CaptureItem.__DurableBinaryBody.AddRoot(capture, item);
            var candidate = capture.Seal();
            discardedId = candidate.Objects.Single(entry => entry.Id == 1)
                .GetState<CaptureHero.__DurableBinaryBody.V1>().Segment1Field1;
            Require(discardedId > 6, "Changed alias needs a new ID.");
            session.Discard(candidate);
            Require(ReferenceEquals(first, session.Current), "Discard preserves parent.");
        }

        uint retriedId;
        using (var capture = session.BeginCapture()) {
            Require(CaptureHero.__DurableBinaryBody.AddRoot(capture, hero) == 1, "Live root ID remains stable.");
            Require(CaptureItem.__DurableBinaryBody.AddRoot(capture, item) == 2, "Other live root remains stable.");
            var candidate = capture.Seal();
            var state = candidate.Objects.Single(entry => entry.Id == 1).GetState<CaptureHero.__DurableBinaryBody.V1>();
            retriedId = state.Segment1Field1;
            Require(retriedId > discardedId && state.Segment0Field1 == 3 && state.Segment1Field2 == 90,
                "Retry burns discarded numbers and retains live strings.");
            session.Accept(candidate);
        }

        using (var capture = session.BeginCapture()) { session.Accept(capture.Seal()); }
        using (var capture = session.BeginCapture()) {
            Require(CaptureHero.__DurableBinaryBody.AddRoot(capture, hero) > retriedId,
                "A retired CLR instance reenters with a fresh ID.");
            session.Discard(capture.Seal());
        }
        Require(session.Current!.Objects.Count == 0, "Discard keeps the empty accepted parent.");
    }

    private static void Require(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
