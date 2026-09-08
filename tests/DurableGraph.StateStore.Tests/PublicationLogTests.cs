using Atelia.Data;
using Atelia.Data.Hashing;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore.Tests;

public sealed class PublicationLogTests : IDisposable {
    private readonly List<string> _paths = [];
    private static readonly PublicationHead First = new(Address(1, 4), new ObjectId(7));
    private static readonly PublicationHead Second = new(Address(1, 32), new ObjectId(7));

    [Fact]
    public void IndependentGoldenBytesFreezeCanonicalRecordAndAddressOrder() {
        // SizedPtr(4,24) interleaves to 0x106; (32,24) to 0x122.
        byte[] first = Convert.FromHexString("010001860207");
        byte[] second = Convert.FromHexString("010101860201A20207");
        Assert.Equal(first, PublicationLog.Encode(null, First));
        Assert.Equal(second, PublicationLog.Encode(First.RevisionAddress, Second));
        Assert.Equal(((FrameAddress?)null, First), PublicationLog.Decode(first));
        Assert.Equal(((FrameAddress?)First.RevisionAddress, Second), PublicationLog.Decode(second));
        for (int length = 0; length < second.Length; length++) {
            byte[] truncated = second[..length];
            Assert.Throws<EndOfStreamException>(() => PublicationLog.Decode(truncated));
        }
    }

    [Theory]
    [InlineData("020001860207")] // Unknown version.
    [InlineData("010201860207")] // Noncanonical Boolean.
    [InlineData("010000860207")] // Zero file number.
    [InlineData("01000100FF01")] // Empty ticket.
    [InlineData("010001840207")] // Ticket shorter than an RBF frame.
    [InlineData("010001820207")] // Ticket starts at the header fence.
    [InlineData("010001860200")] // Null World.
    [InlineData("01000186020700")] // Trailing byte.
    [InlineData("01008100860207")] // Overlong file number.
    [InlineData("01000186820007")] // Overlong ticket.
    [InlineData("01000186028700")] // Overlong World.
    [InlineData("010101860201860207")] // Self parent.
    [InlineData("010101A20201860207")] // Future parent.
    public void MalformedWireFailsClosed(string hex) =>
        Assert.Throws<InvalidDataException>(() => PublicationLog.Decode(Convert.FromHexString(hex)));

    [Fact]
    public void ReplayValidatesEveryRecordWithoutImplicitFlush() {
        string path = NewPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            var recording = new RecordingFile(file);
            int callbacks = 0;
            var log = new PublicationLog(recording, (_, _) => callbacks++);
            Assert.Null(log.Head);
            Assert.Equal(0, recording.Flushes);
            log.Publish(null, First);
            log.Publish(First.RevisionAddress, Second);
            Assert.Equal(Second, log.Head);
            Assert.Equal(2, recording.Flushes);
            Assert.Equal(0, callbacks); // Publish has no external validation callback.
        }
        using IRbfFile reopened = RbfFile.OpenExisting(path);
        var recoveredFile = new RecordingFile(reopened);
        var observed = new List<(FrameAddress?, PublicationHead)>();
        var recovered = new PublicationLog(recoveredFile, (previous, next) => observed.Add((previous, next)));
        Assert.Equal(new[] { ((FrameAddress?)null, First), ((FrameAddress?)First.RevisionAddress, Second) }, observed);
        Assert.Equal(Second, recovered.Head);
        Assert.Equal(0, recoveredFile.Flushes);
        Assert.Throws<InvalidDataException>(() => new PublicationLog(reopened, (_, _) => throw new InvalidDataException("Missing State dependency.")));
    }

    [Fact]
    public void InvalidRequestsHaveNoWriteSideEffectsAndRemainUsable() {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        var recording = new RecordingFile(file);
        var log = new PublicationLog(recording, (_, _) => { });
        Assert.Throws<InvalidDataException>(() => log.Publish(First.RevisionAddress, Second));
        Assert.Throws<InvalidDataException>(() => log.Publish(null, new(default, new ObjectId(7))));
        Assert.Throws<InvalidDataException>(() => log.Publish(null, First with { WorldId = new ObjectId(0) }));
        Assert.Throws<InvalidDataException>(() => log.Publish(null, new(new FrameAddress(1, SizedPtr.Create(0, 24)), new ObjectId(7))));
        Assert.Equal(0, recording.Appends);
        log.Publish(null, First);
        Assert.Throws<InvalidDataException>(() => log.Publish(null, Second));
        Assert.Throws<InvalidDataException>(() => log.Publish(First.RevisionAddress, Second with { WorldId = new ObjectId(8) }));
        Assert.Throws<InvalidDataException>(() => log.Publish(First.RevisionAddress, First));
        Assert.Equal(1, recording.Appends);
        Assert.False(log.IsFaulted);
        log.Publish(First.RevisionAddress, Second);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("disconnected")]
    [InlineData("root-change")]
    public void RecoveryRejectsBrokenPublicationChain(string damage) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        file.Append(PublicationLog.RbfTag, PublicationLog.Encode(null, First)).Unwrap();
        byte[] bad = damage switch {
            "duplicate" => PublicationLog.Encode(null, First),
            "disconnected" => PublicationLog.Encode(Address(1, 8), Second),
            _ => PublicationLog.Encode(First.RevisionAddress, Second with { WorldId = new ObjectId(8) }),
        };
        file.Append(PublicationLog.RbfTag, bad).Unwrap();
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new PublicationLog(file, (_, _) => { }));
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("tail-meta")]
    public void RecoveryRejectsUnexpectedFrameKinds(string damage) {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        file.Append(damage == "tag" ? 42U : PublicationLog.RbfTag,
            PublicationLog.Encode(null, First), damage == "tail-meta" ? new byte[] { 1 } : []).Unwrap();
        file.DurableFlush();
        Assert.Throws<InvalidDataException>(() => new PublicationLog(file, (_, _) => { }));
    }

    [Theory]
    [InlineData("payload-crc")]
    [InlineData("trailer-crc")]
    [InlineData("tombstone")]
    [InlineData("partial-aligned")]
    [InlineData("partial-unaligned")]
    [InlineData("garbage")]
    public void CorruptTailNeverFallsBackOrRepairs(string damage) {
        string path = NewPath();
        long secondOffset;
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            var log = new PublicationLog(file, (_, _) => { });
            log.Publish(null, First);
            secondOffset = file.TailOffset;
            log.Publish(First.RevisionAddress, Second);
        }
        byte[] bytes = File.ReadAllBytes(path);
        switch (damage) {
            case "payload-crc": bytes[(int)secondOffset + 4] ^= 0x20; break;
            case "trailer-crc": bytes[^8] ^= 0x20; break;
            case "partial-aligned": bytes = bytes[..^4]; break;
            case "partial-unaligned": bytes = bytes[..^1]; break;
            case "garbage": bytes = [.. bytes, 1, 2, 3, 4]; break;
            case "tombstone":
                Span<byte> trailer = bytes.AsSpan(bytes.Length - 20, 16);
                trailer[7] |= 0x80;
                RollingCrc.SealCodewordBackward(trailer);
                break;
        }
        File.WriteAllBytes(path, bytes);
        Assert.ThrowsAny<Exception>(() => {
            using IRbfFile reopened = RbfFile.OpenExisting(path);
            _ = new PublicationLog(reopened, (_, _) => { });
        });
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(FailurePoint.BeforeAppend, false)]
    [InlineData(FailurePoint.AfterAppend, true)]
    [InlineData(FailurePoint.BeforeFlush, true)]
    [InlineData(FailurePoint.AfterFlush, true)]
    public void UncertainFailureFaultsFacadeAndRecoveryObservesCompleteRecords(FailurePoint failure, bool secondVisible) {
        string path = NewPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            new PublicationLog(file, (_, _) => { }).Publish(null, First);
            var recording = new RecordingFile(file) { Failure = failure };
            var log = new PublicationLog(recording, (_, _) => { });
            Assert.Throws<IOException>(() => log.Publish(First.RevisionAddress, Second));
            Assert.True(log.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => log.Head);
            Assert.Throws<InvalidOperationException>(() => log.Publish(First.RevisionAddress, Second));
            Assert.Equal(1, recording.Appends);
        }
        using IRbfFile reopened = RbfFile.OpenExisting(path);
        var recovered = new PublicationLog(reopened, (_, _) => { });
        Assert.Equal(secondVisible ? Second : First, recovered.Head);
        // This confirms complete visible bytes; it does not simulate a power loss.
        reopened.DurableFlush();
    }

    [Fact]
    public void PartialAppendFaultsFacadeAndRequiresExplicitRepairOutsideRecovery() {
        string path = NewPath();
        using (IRbfFile file = RbfFile.CreateNew(path)) {
            new PublicationLog(file, (_, _) => { }).Publish(null, First);
            var log = new PublicationLog(new RecordingFile(file) { Failure = FailurePoint.PartialAppend }, (_, _) => { });
            Assert.Throws<IOException>(() => log.Publish(First.RevisionAddress, Second));
            Assert.True(log.IsFaulted);
        }
        byte[] torn = File.ReadAllBytes(path);
        Assert.ThrowsAny<Exception>(() => {
            using IRbfFile reopened = RbfFile.OpenExisting(path);
            _ = new PublicationLog(reopened, (_, _) => { });
        });
        Assert.Equal(torn, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExternalTailChangesInvalidateTheOwner() {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        var log = new PublicationLog(file, (_, _) => { });
        file.Append(PublicationLog.RbfTag, PublicationLog.Encode(null, First)).Unwrap();
        Assert.Throws<InvalidOperationException>(() => log.RequireAvailable());
        Assert.True(log.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => log.Publish(null, First));
    }

    [Fact]
    public void LaterSegmentMayRestartOffsetsButEarlierSegmentIsRejected() {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        var log = new PublicationLog(file, (_, _) => { });
        log.Publish(null, Second);
        var laterFile = new PublicationHead(Address(2, 4), new ObjectId(7));
        log.Publish(Second.RevisionAddress, laterFile);
        Assert.Equal(laterFile, log.Head);
        Assert.Throws<InvalidDataException>(() => log.Publish(laterFile.RevisionAddress,
            new PublicationHead(Address(1, 64), new ObjectId(7))));
        Assert.False(log.IsFaulted);
    }

    [Fact]
    public void AfterAppendCheckpointFailureHasAnUncertainOutcome() {
        using IRbfFile file = RbfFile.CreateNew(NewPath());
        var recording = new RecordingFile(file);
        var log = new PublicationLog(recording, (_, _) => { }) {
            AfterAppend = () => throw new IOException("Interrupted before the publication barrier."),
        };
        Assert.Throws<IOException>(() => log.Publish(null, First));
        Assert.True(log.IsFaulted);
        Assert.Equal(1, recording.Appends);
        Assert.Equal(0, recording.Flushes);
        Assert.Equal(First, new PublicationLog(file, (_, _) => { }).Head);
    }

    private static FrameAddress Address(uint file, long offset) => new(file, SizedPtr.Create(offset, 24));

    private string NewPath() {
        string path = Path.Combine(Path.GetTempPath(), $"publication-log-{Guid.NewGuid():N}.rbf");
        _paths.Add(path);
        return path;
    }

    public void Dispose() {
        foreach (string path in _paths) { File.Delete(path); }
    }

    public enum FailurePoint { None, BeforeAppend, AfterAppend, PartialAppend, BeforeFlush, AfterFlush }

    private sealed class RecordingFile(IRbfFile inner) : IRbfFile {
        public FailurePoint Failure { get; init; }
        public int Appends { get; private set; }
        public int Flushes { get; private set; }
        public long TailOffset => inner.TailOffset;
        public AteliaResult<SizedPtr> Append(uint tag, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tailMeta = default) {
            Appends++;
            if (Failure == FailurePoint.BeforeAppend) { throw new IOException("Before append."); }
            var result = inner.Append(tag, payload, tailMeta);
            if (Failure == FailurePoint.PartialAppend) {
                inner.Truncate(inner.TailOffset - 4);
                throw new IOException("Partial append.");
            }
            if (Failure == FailurePoint.AfterAppend) { throw new IOException("After append."); }
            return result;
        }
        public void DurableFlush() {
            Flushes++;
            if (Failure == FailurePoint.BeforeFlush) { throw new IOException("Before flush."); }
            inner.DurableFlush();
            if (Failure == FailurePoint.AfterFlush) { throw new IOException("After flush."); }
        }
        public RbfFrameBuilder BeginAppend() => inner.BeginAppend();
        public AteliaResult<RbfPooledFrame> ReadPooledFrame(SizedPtr ptr) => inner.ReadPooledFrame(ptr);
        public AteliaResult<RbfFrame> ReadFrame(SizedPtr ptr, Span<byte> buffer) => inner.ReadFrame(ptr, buffer);
        public RbfReverseSequence ScanReverse(bool showTombstone = false) => inner.ScanReverse(showTombstone);
        public RbfForwardSequence ScanForward(bool showTombstone = false) => inner.ScanForward(showTombstone);
        public long GetPhysicalOffsetImmediatelyAfter(SizedPtr ticket) => inner.GetPhysicalOffsetImmediatelyAfter(ticket);
        public AteliaResult<OptionalRbfFrameInfo> ReadFrameInfoImmediatelyAfter(SizedPtr ticket) => inner.ReadFrameInfoImmediatelyAfter(ticket);
        public AteliaResult<RbfFrameInfo> ReadFrameInfo(SizedPtr ticket) => inner.ReadFrameInfo(ticket);
        public AteliaResult<RbfTailMeta> ReadTailMeta(SizedPtr ticket, Span<byte> buffer) => inner.ReadTailMeta(ticket, buffer);
        public AteliaResult<RbfPooledTailMeta> ReadPooledTailMeta(SizedPtr ticket) => inner.ReadPooledTailMeta(ticket);
        public void Truncate(long newLengthBytes) => inner.Truncate(newLengthBytes);
        public void SetupReadLog(string? logPath) => inner.SetupReadLog(logPath);
        public void Dispose() => inner.Dispose();
    }
}
