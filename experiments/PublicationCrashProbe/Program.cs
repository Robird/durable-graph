using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

// This witness uses only tiny ordinal records, not the Repository publication wire.
// The controller kills the writer after READY; none of its using blocks unwind.
const uint probeTag = 0x50524F42;
if (args.Length != 3) { throw new ArgumentException("Usage: child|verify directory checkpoint|expected"); }
string root = Path.GetFullPath(args[1]);
string publicationPath = Path.Combine(root, "publication.rbf");
string statePath = Path.Combine(root, "state");
var options = new RbfSegmentStoreOptions { SegmentSizeThresholdBytes = 32, RecoverActiveTailOnOpen = false };
if (args[0] == "child") {
    Directory.CreateDirectory(root);
    using SegmentStore states = SegmentStore.CreateNew(statePath, options);
    using IRbfFile publication = RbfFile.CreateNew(publicationPath);
    publication.DurableFlush();
    if (args[2] == "empty") { WaitForKill(); }
    AppendState(states, 1);
    publication.Append(probeTag, new byte[] { 1, 1 }).Unwrap();
    publication.DurableFlush();
    AppendState(states, 2); // Opens a newly created segment and flushes that exact writer lease.
    if (args[2] == "before-publication") { WaitForKill(); }
    publication.Append(probeTag, new byte[] { 1, 2 }).Unwrap();
    if (args[2] == "after-append") { WaitForKill(); }
    publication.DurableFlush();
    if (args[2] == "after-flush") { WaitForKill(); }
    throw new ArgumentException("Unknown child checkpoint.");
}
if (args[0] == "verify") {
    int head;
    try {
        using SegmentStore states = SegmentStore.OpenExisting(statePath, options);
        // Writable RBF open alone does not validate a tail. Check every frame explicitly.
        for (uint segment = 1; segment <= states.ActiveSegmentNumber; segment++) {
            using RbfSegmentReaderLease reader = states.OpenReader(segment);
            _ = ReadStrict(reader.File, publication: false);
        }
        using IRbfFile publication = RbfFile.OpenExisting(publicationPath);
        head = ReadStrict(publication, publication: true);
        if (head != 0) {
            using RbfSegmentReaderLease reader = states.OpenReader((uint)head);
            if (ReadStrict(reader.File, publication: false) != head) {
                throw new InvalidDataException("Published state is absent or does not match.");
            }
        }
        publication.DurableFlush(); // Confirm complete bytes after a potentially uncertain writer result.
    }
    catch when (args[2] == "invalid") {
        Console.WriteLine("StrictInvalid=True");
        return;
    }
    if (!int.TryParse(args[2], out int expected) || head != expected) {
        throw new InvalidDataException($"Expected {args[2]}, observed head {head}.");
    }
    Console.WriteLine($"ObservedHead={head}");
    return;
}
throw new ArgumentException("Unknown mode.");

static void AppendState(SegmentStore states, byte ordinal) {
    using RbfSegmentWriterLease writer = states.OpenActiveWriter();
    if (writer.SegmentNumber != ordinal) { throw new InvalidDataException("Rollover witness did not rotate."); }
    writer.File.Append(probeTag, new byte[] { 1, ordinal }).Unwrap();
    writer.File.DurableFlush();
}

static int ReadStrict(IRbfFile file, bool publication) {
    int head = 0;
    var frames = file.ScanForward(showTombstone: true).GetEnumerator();
    while (frames.MoveNext()) {
        using RbfPooledFrame frame = file.ReadPooledFrame(frames.Current.Ticket).Unwrap();
        ReadOnlySpan<byte> payload = frame.PayloadAndMeta;
        if (frame.IsTombstone || frame.Tag != probeTag || frame.TailMetaLength != 0 ||
            payload.Length != 2 || payload[0] != 1 || payload[1] == 0 ||
            (publication && payload[1] != head + 1)) {
            throw new InvalidDataException("Invalid probe record.");
        }
        head = payload[1];
    }
    if (frames.TerminationError is { } error) { throw new InvalidDataException(error.Message); }
    return head;
}

static void WaitForKill() {
    Console.WriteLine("READY");
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    throw new InvalidOperationException("Writer must be killed externally.");
}
