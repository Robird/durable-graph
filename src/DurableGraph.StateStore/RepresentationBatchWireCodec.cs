using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

internal static class RepresentationBatchWireCodec {
    internal const uint RbfTag = 0x31425052; // RPB1 in little-endian byte order.
    internal const byte Version = 1;

    internal static byte[] Write(IReadOnlyList<KeyValuePair<RepresentationId, ObjectLayout>> entries) {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) { throw new ArgumentException("A representation batch must not be empty.", nameof(entries)); }
        var buffer = new LimitedBufferWriter();
        var writer = new BinaryPayloadWriter(buffer);
        writer.WriteByte(Version);
        writer.WriteUInt32((uint)entries.Count);
        uint previous = 1;
        var layouts = new HashSet<ObjectLayout>();
        foreach ((RepresentationId id, ObjectLayout layout) in entries) {
            ArgumentNullException.ThrowIfNull(layout);
            if (id.Value <= previous || layout.Kind == ObjectStateKind.String || !layouts.Add(layout)) {
                throw new ArgumentException("Representation rows require increasing IDs above one and distinct non-string layouts.", nameof(entries));
            }
            writer.WriteUInt32(id.Value);
            RepresentationDescriptorCodec.Write(ref writer, layout);
            previous = id.Value;
        }
        return buffer.ToArray();
    }

    internal static KeyValuePair<RepresentationId, ObjectLayout>[] Read(ReadOnlySpan<byte> payload,
        Func<SchemaKey, DurableSchema> resolveSchema) {
        var reader = new BinaryPayloadReader(payload);
        if (reader.ReadByte() != Version) { throw new InvalidDataException("Unknown representation batch version."); }
        uint count = reader.ReadUInt32();
        // Every legal row requires at least an ID and the four-byte array descriptor.
        if (count == 0 || count > (uint)reader.RemainingCount / 5) {
            throw new InvalidDataException("Invalid representation batch count.");
        }
        var entries = new KeyValuePair<RepresentationId, ObjectLayout>[(int)count];
        uint previous = 1;
        var layouts = new HashSet<ObjectLayout>();
        for (int i = 0; i < entries.Length; i++) {
            uint id = reader.ReadUInt32();
            if (id <= previous) { throw new InvalidDataException("Representation IDs must be strictly increasing and greater than one."); }
            ObjectLayout layout = RepresentationDescriptorCodec.Read(ref reader, resolveSchema);
            if (layout.Kind == ObjectStateKind.String || !layouts.Add(layout)) {
                throw new InvalidDataException("Representation batches require distinct non-string layouts.");
            }
            entries[i] = new(new(id), layout);
            previous = id;
        }
        reader.EnsureFullyConsumed();
        return entries;
    }

    private sealed class LimitedBufferWriter : IBufferWriter<byte> {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public void Advance(int count) { Check(count); _buffer.Advance(count); }
        public Memory<byte> GetMemory(int sizeHint = 0) { Check(Math.Max(sizeHint, 1)); return _buffer.GetMemory(sizeHint); }
        public Span<byte> GetSpan(int sizeHint = 0) { Check(Math.Max(sizeHint, 1)); return _buffer.GetSpan(sizeHint); }
        private void Check(int size) {
            if (size < 0 || size > RbfFile.MaxPayloadAndMetaLength - _buffer.WrittenCount) {
                throw new ArgumentException("Representation batch exceeds the maximum RBF payload length.");
            }
        }
        internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    }
}
