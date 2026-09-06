using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

internal static class SchemaBatchWireCodec {
    internal const uint RbfTag = 0x31424753; // SGB1 in little-endian byte order.
    internal const byte Version = 1;
    internal const int MaximumDepth = 256;

    internal static byte[] Write(IReadOnlyCollection<DurableSchema> schemas) {
        if (schemas.Count == 0) {
            throw new ArgumentException("A registration frame must contain at least one definition.", nameof(schemas));
        }
        var buffer = new LimitedBufferWriter();
        var writer = new BinaryPayloadWriter(buffer);
        writer.WriteByte(Version);
        writer.WriteUInt32((uint)schemas.Count);
        foreach (DurableSchema schema in schemas.OrderBy(static x => x.SchemaId, StringComparer.Ordinal).ThenBy(static x => x.Version)) {
            SchemaKeyWireCodec.Write(ref writer, Key(schema));
            writer.WriteBoolean(schema.BaseSchema is not null);
            if (schema.BaseSchema is { } ancestor) {
                SchemaKeyWireCodec.Write(ref writer, Key(ancestor));
            }
            writer.WriteUInt32((uint)schema.Fields.Length);
            foreach (DurableFieldInfo field in schema.Fields) {
                writer.WriteUInt32((uint)field.FieldId);
                writer.WriteByte(EncodeType(field.TypeTag));
            }
        }
        return buffer.ToArray();
    }

    internal static Dictionary<SchemaKey, DurableSchema> Read(
        ReadOnlySpan<byte> payload,
        IReadOnlyDictionary<SchemaKey, DurableSchema> registered) {
        var reader = new BinaryPayloadReader(payload);
        if (reader.ReadByte() != Version) {
            throw new InvalidDataException("Unknown Schema batch version.");
        }
        uint count = reader.ReadUInt32();
        // Even the smallest row needs a key, base flag and field count.
        if (count == 0 || count > (uint)reader.RemainingCount / 5) {
            throw new InvalidDataException("Invalid Schema batch definition count.");
        }
        var declarations = new Dictionary<SchemaKey, Declaration>();
        SchemaKey? previous = null;
        for (uint index = 0; index < count; index++) {
            SchemaKey key = SchemaKeyWireCodec.Read(ref reader);
            if (previous is { } prior && Compare(prior, key) >= 0) {
                throw new InvalidDataException("Schema definitions must be strictly ordered by identity and version.");
            }
            previous = key;
            SchemaKey? baseKey = reader.ReadBoolean() ? SchemaKeyWireCodec.Read(ref reader) : null;
            uint fieldCount = reader.ReadUInt32();
            if (fieldCount > (uint)reader.RemainingCount / 2) {
                throw new InvalidDataException("Invalid Schema field count.");
            }
            var fields = new DurableFieldInfo[(int)fieldCount];
            int previousField = 0;
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++) {
                uint fieldId = reader.ReadUInt32();
                if (fieldId > int.MaxValue || fieldId <= previousField) {
                    throw new InvalidDataException("Schema fields require positive, strictly increasing Int32 identifiers.");
                }
                previousField = (int)fieldId;
                fields[fieldIndex] = new DurableFieldInfo((int)fieldId, DecodeType(reader.ReadByte()));
            }
            declarations.Add(key, new Declaration(baseKey, fields));
        }
        reader.EnsureFullyConsumed();

        var resolved = new Dictionary<SchemaKey, DurableSchema>();
        foreach (SchemaKey key in declarations.Keys) {
            Resolve(key, declarations, registered, resolved);
        }
        var merged = new Dictionary<SchemaKey, DurableSchema>(registered);
        foreach ((SchemaKey key, DurableSchema schema) in resolved) {
            if (merged.TryGetValue(key, out DurableSchema? old)) {
                if (!old.Equals(schema)) {
                    throw new InvalidDataException($"Conflicting persisted Schema '{key.SchemaId}' version {key.Version}.");
                }
            }
            else {
                merged.Add(key, schema);
            }
        }
        return merged;
    }

    private static void Resolve(
        SchemaKey key,
        Dictionary<SchemaKey, Declaration> declarations,
        IReadOnlyDictionary<SchemaKey, DurableSchema> registered,
        Dictionary<SchemaKey, DurableSchema> resolved) {
        if (resolved.ContainsKey(key)) { return; }
        var path = new List<SchemaKey>();
        var seen = new HashSet<SchemaKey>();
        DurableSchema? ancestor = null;
        SchemaKey? cursor = key;
        while (cursor is { } next) {
            if (resolved.TryGetValue(next, out ancestor)) { break; }
            if (!declarations.TryGetValue(next, out Declaration? declaration)) {
                if (!registered.TryGetValue(next, out ancestor)) {
                    throw new InvalidDataException($"Missing exact base Schema '{next.SchemaId}' version {next.Version}.");
                }
                break;
            }
            if (!seen.Add(next) || path.Count >= MaximumDepth) {
                throw new InvalidDataException("Schema inheritance is cyclic or exceeds the maximum depth.");
            }
            path.Add(next);
            cursor = declaration.BaseKey;
        }
        int depth = path.Count;
        for (DurableSchema? item = ancestor; item is not null; item = item.BaseSchema) {
            if (++depth > MaximumDepth) {
                throw new InvalidDataException("Schema inheritance exceeds the maximum depth.");
            }
        }
        for (int index = path.Count - 1; index >= 0; index--) {
            SchemaKey item = path[index];
            try {
                ancestor = new DurableSchema(item.SchemaId, item.Version, declarations[item].Fields, ancestor);
            }
            catch (ArgumentException error) {
                throw new InvalidDataException("Invalid persisted Schema inheritance.", error);
            }
            resolved.Add(item, ancestor);
        }
    }

    internal static SchemaKey Key(DurableSchema schema) => new(schema.SchemaId, schema.Version);

    private static int Compare(SchemaKey left, SchemaKey right) {
        int identity = StringComparer.Ordinal.Compare(left.SchemaId, right.SchemaId);
        return identity == 0 ? left.Version.CompareTo(right.Version) : identity;
    }

    private static byte EncodeType(TypeTag tag) => tag switch {
        TypeTag.Boolean => 1, TypeTag.Int32 => 2, TypeTag.Int64 => 3,
        TypeTag.String => 4, TypeTag.Byte => 5, TypeTag.SByte => 6,
        TypeTag.Int16 => 7, TypeTag.UInt16 => 8, TypeTag.UInt32 => 9,
        TypeTag.UInt64 => 10, TypeTag.Char => 11, TypeTag.Half => 12,
        TypeTag.Single => 13, TypeTag.Double => 14,
        _ => throw new ArgumentException("Unsupported Schema field type.", nameof(tag)),
    };

    private static TypeTag DecodeType(byte tag) => tag switch {
        1 => TypeTag.Boolean, 2 => TypeTag.Int32, 3 => TypeTag.Int64,
        4 => TypeTag.String, 5 => TypeTag.Byte, 6 => TypeTag.SByte,
        7 => TypeTag.Int16, 8 => TypeTag.UInt16, 9 => TypeTag.UInt32,
        10 => TypeTag.UInt64, 11 => TypeTag.Char, 12 => TypeTag.Half,
        13 => TypeTag.Single, 14 => TypeTag.Double,
        _ => throw new InvalidDataException($"Unknown Schema field type code {tag}."),
    };

    private sealed record Declaration(SchemaKey? BaseKey, DurableFieldInfo[] Fields);

    private sealed class LimitedBufferWriter : IBufferWriter<byte> {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public void Advance(int count) {
            Check(count);
            _buffer.Advance(count);
        }
        public Memory<byte> GetMemory(int sizeHint = 0) {
            Check(Math.Max(sizeHint, 1));
            return _buffer.GetMemory(sizeHint);
        }
        public Span<byte> GetSpan(int sizeHint = 0) {
            Check(Math.Max(sizeHint, 1));
            return _buffer.GetSpan(sizeHint);
        }
        private void Check(int size) {
            if (size < 0 || size > RbfFile.MaxPayloadAndMetaLength - _buffer.WrittenCount) {
                throw new ArgumentException("Schema batch exceeds the maximum RBF payload length.");
            }
        }
        internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    }
}
