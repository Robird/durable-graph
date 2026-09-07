using System.Buffers;
using Atelia.Data;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

internal sealed record PublicationHead(FrameAddress RevisionAddress, uint WorldId);

/// <summary>A strict append-only publication chain owned by one repository writer.</summary>
/// <remarks>
/// Recovery validates all records and referenced State before the repository confirms
/// Schema, State and publication durability in that order. Construction never flushes
/// or repairs a tail. An uncertain append/flush outcome requires disposal and reopen.
/// </remarks>
internal sealed class PublicationLog {
    internal const uint RbfTag = 0x44475048;
    private const byte Version = 1;
    private readonly IRbfFile _file;
    private PublicationHead? _head;
    private long _acceptedTail;
    private bool _busy;

    internal PublicationLog(IRbfFile file, Action<FrameAddress?, PublicationHead> validate) {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(validate);
        _file = file;
        long originalTail = file.TailOffset;
        var frames = file.ScanForward(showTombstone: true).GetEnumerator();
        while (frames.MoveNext()) {
            using RbfPooledFrame frame = file.ReadPooledFrame(frames.Current.Ticket).Unwrap();
            if (frame.IsTombstone || frame.Tag != RbfTag || frame.TailMetaLength != 0) {
                throw new InvalidDataException("Unexpected frame in the dedicated publication file.");
            }
            (FrameAddress? previous, PublicationHead next) = Decode(frame.PayloadAndMeta);
            ValidateTransition(_head, previous, next);
            validate(previous, next);
            _head = next;
        }
        if (frames.TerminationError is { } error) {
            throw new InvalidDataException($"Invalid publication framing; no automatic tail recovery is performed. {error.Message}");
        }
        _acceptedTail = originalTail;
        RequireUnchangedTail();
    }

    internal PublicationHead? Head {
        get {
            RequireAvailable();
            return _head;
        }
    }

    internal bool IsFaulted { get; private set; }

    // Repository tests can interrupt the publication uncertainty window.
    internal Action? AfterAppend { get; set; }

    internal void Publish(FrameAddress? expectedParent, PublicationHead next) {
        RequireAvailable();
        ArgumentNullException.ThrowIfNull(next);
        ValidateTransition(_head, expectedParent, next);
        byte[] payload = Encode(expectedParent, next);
        RequireUnchangedTail();
        _busy = true;
        try {
            _file.Append(RbfTag, payload).Unwrap();
            AfterAppend?.Invoke();
            _file.DurableFlush();
            _acceptedTail = _file.TailOffset;
            _head = next;
        }
        catch {
            IsFaulted = true;
            throw;
        }
        finally {
            _busy = false;
        }
    }

    internal void RequireAvailable() {
        if (IsFaulted) { throw new InvalidOperationException("Publication outcome is uncertain; reopen the repository before further use."); }
        if (_busy) { throw new InvalidOperationException("Publication operations cannot be reentered."); }
        RequireUnchangedTail();
    }

    private void RequireUnchangedTail() {
        if (_file.TailOffset != _acceptedTail) {
            IsFaulted = true;
            throw new InvalidOperationException("The publication file was changed outside its owning repository; reopen before further use.");
        }
    }

    internal static byte[] Encode(FrameAddress? previous, PublicationHead next) {
        ArgumentNullException.ThrowIfNull(next);
        ValidateRecord(previous, next);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BinaryPayloadWriter(buffer);
        writer.WriteByte(Version);
        writer.WriteBoolean(previous.HasValue);
        if (previous is { } parent) { WriteAddress(ref writer, parent); }
        WriteAddress(ref writer, next.RevisionAddress);
        writer.WriteUInt32(next.WorldId);
        return buffer.WrittenSpan.ToArray();
    }

    internal static (FrameAddress? Previous, PublicationHead Next) Decode(ReadOnlySpan<byte> payload) {
        var reader = new BinaryPayloadReader(payload);
        if (reader.ReadByte() != Version) { throw new InvalidDataException("Unknown publication version."); }
        FrameAddress? previous = reader.ReadBoolean() ? ReadAddress(ref reader) : null;
        var next = new PublicationHead(ReadAddress(ref reader), reader.ReadUInt32());
        reader.EnsureFullyConsumed();
        ValidateRecord(previous, next);
        return (previous, next);
    }

    private static void ValidateTransition(PublicationHead? current, FrameAddress? previous, PublicationHead next) {
        ValidateRecord(previous, next);
        if (previous != current?.RevisionAddress) {
            throw new InvalidDataException("Publication previous head must exactly match the current head.");
        }
        if (current is not null && current.WorldId != next.WorldId) {
            throw new InvalidDataException("The World object identity cannot change within a repository.");
        }
    }

    private static void ValidateRecord(FrameAddress? previous, PublicationHead next) {
        ValidateAddress(next.RevisionAddress);
        if (next.WorldId == 0) { throw new InvalidDataException("A published World requires a nonzero object identity."); }
        if (previous is { } parent) {
            ValidateAddress(parent);
            FrameAddress address = next.RevisionAddress;
            if (address.FileNumber < parent.FileNumber ||
                (address.FileNumber == parent.FileNumber && address.FrameTicket.Offset <= parent.FrameTicket.Offset)) {
                throw new InvalidDataException("A published Revision must be strictly later than its previous head.");
            }
        }
    }

    private static void ValidateAddress(FrameAddress address) {
        // RBF1 has a four-byte initial fence and a minimum 24-byte frame.
        if (address.FileNumber == 0 || address.FrameTicket.Offset < 4 || address.FrameTicket.Length < 24) {
            throw new InvalidDataException("Publication requires a valid absolute RBF frame address.");
        }
    }

    private static void WriteAddress(ref BinaryPayloadWriter writer, FrameAddress address) {
        writer.WriteUInt32(address.FileNumber);
        writer.WriteUInt64(address.FrameTicket.Serialize());
    }

    private static FrameAddress ReadAddress(ref BinaryPayloadReader reader) {
        uint fileNumber = reader.ReadUInt32();
        SizedPtr ticket = SizedPtr.Deserialize(reader.ReadUInt64());
        if (fileNumber == 0 || ticket.Offset < 4 || ticket.Length < 24) {
            throw new InvalidDataException("Invalid persisted publication address.");
        }
        return new FrameAddress(fileNumber, ticket);
    }
}
