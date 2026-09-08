using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

internal static class SchemaBatchWireCodec {
    internal const uint RbfTag = 0x31424753; // SGB1 in little-endian byte order.
    internal const byte Version = 4;
    internal const int MaximumDepth = 256;

    internal static byte[] Write(IReadOnlyCollection<DurableSchema> schemas) {
        if (schemas.Count == 0) {
            throw new ArgumentException("A registration frame must contain at least one definition.", nameof(schemas));
        }
        var buffer = new LimitedBufferWriter();
        var writer = new BinaryPayloadWriter(buffer);
        writer.WriteByte(Version);
        writer.WriteUInt32((uint)schemas.Count);
        foreach (DurableSchema schema in schemas.OrderBy(static x => x.Type).ThenBy(static x => x.Version)) {
            SchemaKeyWireCodec.Write(ref writer, Key(schema));
            writer.WriteByte((byte)schema.Kind);
            writer.WriteBoolean(schema.BaseSchema is not null);
            if (schema.BaseSchema is { } ancestor) {
                SchemaKeyWireCodec.Write(ref writer, Key(ancestor));
            }
            writer.WriteUInt32((uint)schema.Fields.Length);
            foreach (DurableFieldInfo field in schema.Fields) {
                writer.WriteUInt32((uint)field.FieldId);
                writer.WriteByte(EncodeType(field.TypeTag));
                if (field.TypeTag == TypeTag.ObjectReference) {
                    TypeExprWireCodec.Write(ref writer, field.TargetType!);
                }
                else if (field.TypeTag == TypeTag.InlineValue) {
                    SchemaKeyWireCodec.Write(ref writer, Key(field.InlineSchema!));
                }
            }
        }
        return buffer.ToArray();
    }

    internal static Dictionary<SchemaKey, DurableSchema> Read(
        ReadOnlySpan<byte> payload,
        IReadOnlyDictionary<SchemaKey, DurableSchema> registered) {
        var reader = new BinaryPayloadReader(payload);
        byte version = reader.ReadByte();
        if (version is < 1 or > Version) {
            throw new InvalidDataException("Unknown Schema batch version.");
        }
        uint count = reader.ReadUInt32();
        int minimumRowLength = version == 1 ? 5 : 6;
        if (count == 0 || count > (uint)reader.RemainingCount / minimumRowLength) {
            throw new InvalidDataException("Invalid Schema batch definition count.");
        }
        var declarations = new Dictionary<SchemaKey, Declaration>();
        var familyKinds = new Dictionary<string, SchemaKind>(StringComparer.Ordinal);
        var familyArities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DurableSchema schema in registered.Values) {
            ValidateDeclaration(familyKinds, familyArities, schema.Type, schema.Kind);
            foreach (DurableFieldInfo field in schema.Fields) {
                if (field.TargetType is { } target) {
                    ValidateDeclaration(familyKinds, familyArities, target, SchemaKind.ReferenceObject);
                }
            }
        }
        SchemaKey? previous = null;
        for (uint index = 0; index < count; index++) {
            SchemaKey key = ReadKey(ref reader, version);
            if (previous is { } prior && Compare(prior, key) >= 0) {
                throw new InvalidDataException("Schema definitions must be strictly ordered by identity and version.");
            }
            previous = key;
            SchemaKind kind = version == 1 ? SchemaKind.ReferenceObject : reader.ReadByte() switch {
                1 => SchemaKind.ReferenceObject,
                2 => SchemaKind.InlineValue,
                _ => throw new InvalidDataException("Unknown Schema kind."),
            };
            ValidateDeclaration(familyKinds, familyArities, key.Type, kind);
            SchemaKey? baseKey = reader.ReadBoolean() ? ReadKey(ref reader, version) : null;
            if (baseKey is { } baseIdentity) { ValidateTypeArities(familyArities, baseIdentity.Type); }
            if (kind == SchemaKind.InlineValue && baseKey.HasValue) {
                throw new InvalidDataException("An inline Schema cannot have a base Schema.");
            }
            uint fieldCount = reader.ReadUInt32();
            if (fieldCount > (uint)reader.RemainingCount / 2) {
                throw new InvalidDataException("Invalid Schema field count.");
            }
            var fields = new FieldDeclaration[(int)fieldCount];
            int previousField = 0;
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++) {
                uint fieldId = reader.ReadUInt32();
                if (fieldId > int.MaxValue || fieldId <= previousField) {
                    throw new InvalidDataException("Schema fields require positive, strictly increasing Int32 identifiers.");
                }
                previousField = (int)fieldId;
                TypeTag tag = DecodeType(reader.ReadByte());
                if (version == 1 && tag == TypeTag.InlineValue) {
                    throw new InvalidDataException("Version 1 Schema batches cannot contain inline fields.");
                }
                TypeExpr? targetType = null;
                if (tag == TypeTag.ObjectReference) {
                    if (version < 3) {
                        string targetId = reader.ReadString();
                        if (string.IsNullOrWhiteSpace(targetId)) {
                            throw new InvalidDataException("A durable reference requires a nonblank nominal Schema identity.");
                        }
                        targetType = TypeExpr.Named(targetId);
                    }
                    else { targetType = TypeExprWireCodec.Read(ref reader, allowArrays: version >= 4); }
                    if (targetType.Kind != TypeExprKind.Named && !targetType.IsArray) {
                        throw new InvalidDataException("An object reference requires a named or array nominal type.");
                    }
                    ValidateDeclaration(familyKinds, familyArities, targetType, SchemaKind.ReferenceObject);
                }
                SchemaKey? inlineKey = tag == TypeTag.InlineValue ? ReadKey(ref reader, version) : null;
                if (inlineKey is { } inlineIdentity) { ValidateTypeArities(familyArities, inlineIdentity.Type); }
                fields[fieldIndex] = new((int)fieldId, tag, targetType, inlineKey);
            }
            declarations.Add(key, new(kind, baseKey, fields));
        }
        reader.EnsureFullyConsumed();

        // Validate the entire base + inline dependency DAG before constructing any Schema.
        // Height caching bounds shared diamonds without hiding a deeper path to a cached node.
        var heights = new Dictionary<SchemaKey, int>();
        var visiting = new HashSet<SchemaKey>();
        var order = new List<SchemaKey>();
        foreach (SchemaKey key in declarations.Keys) { ValidateLayout(key, 1); }

        var merged = new Dictionary<SchemaKey, DurableSchema>(registered);
        foreach (SchemaKey key in order) {
            Declaration row = declarations[key];
            DurableSchema? ancestor = row.BaseKey is { } baseKey ? merged[baseKey] : null;
            DurableFieldInfo[] fields = row.Fields.Select(field => field.Tag == TypeTag.ObjectReference
                ? DurableFieldInfo.Reference(field.FieldId, field.TargetType!)
                : new DurableFieldInfo(field.FieldId, field.Tag,
                    inlineSchema: field.InlineKey is { } inlineKey ? merged[inlineKey] : null)).ToArray();
            DurableSchema schema;
            try {
                schema = new DurableSchema(key.Type, key.Version, fields, ancestor, row.Kind);
            }
            catch (ArgumentException error) {
                throw new InvalidDataException("Invalid persisted Schema layout.", error);
            }
            if (merged.TryGetValue(key, out DurableSchema? old)) {
                if (!old.Equals(schema)) {
                    throw new InvalidDataException($"Conflicting persisted Schema '{key.SchemaId}' version {key.Version}.");
                }
            }
            else { merged.Add(key, schema); }
        }
        return merged;

        int ValidateLayout(SchemaKey key, int depth) {
            if (depth > MaximumDepth) { throw new InvalidDataException("Schema layout exceeds the maximum depth."); }
            if (heights.TryGetValue(key, out int cached)) {
                if (depth + cached - 1 > MaximumDepth) { throw new InvalidDataException("Schema layout exceeds the maximum depth."); }
                return cached;
            }
            if (!visiting.Add(key)) { throw new InvalidDataException("Schema layout is cyclic."); }
            int height = 1;
            if (declarations.TryGetValue(key, out Declaration? row)) {
                if (row.BaseKey is { } ancestor) { VisitDependency(ancestor, SchemaKind.ReferenceObject); }
                foreach (FieldDeclaration field in row.Fields) {
                    if (field.InlineKey is { } inline) { VisitDependency(inline, SchemaKind.InlineValue); }
                }
                order.Add(key);
            }
            else if (registered.TryGetValue(key, out DurableSchema? existing)) {
                if (existing.BaseSchema is { } ancestor) { VisitDependency(Key(ancestor), SchemaKind.ReferenceObject); }
                foreach (DurableFieldInfo field in existing.Fields) {
                    if (field.InlineSchema is { } inline) { VisitDependency(Key(inline), SchemaKind.InlineValue); }
                }
            }
            else { throw new InvalidDataException($"Missing exact layout Schema '{key.SchemaId}' version {key.Version}."); }
            visiting.Remove(key);
            heights.Add(key, height);
            return height;

            void VisitDependency(SchemaKey dependency, SchemaKind requiredKind) {
                SchemaKind actualKind = declarations.TryGetValue(dependency, out Declaration? declaration)
                    ? declaration.Kind
                    : registered.TryGetValue(dependency, out DurableSchema? schema) ? schema.Kind
                    : throw new InvalidDataException($"Missing exact layout Schema '{dependency.SchemaId}' version {dependency.Version}.");
                if (actualKind != requiredKind) { throw new InvalidDataException("Schema layout dependency has the wrong kind."); }
                height = Math.Max(height, 1 + ValidateLayout(dependency, depth + 1));
            }
        }
    }

    internal static void ValidateDeclaration(Dictionary<string, SchemaKind> families, Dictionary<string, int> arities,
        TypeExpr type, SchemaKind kind) {
        if (type.IsArray) {
            // A nominal array reference does not assert its element's Schema kind:
            // a named element can itself be either a reference or an inline value.
            ValidateTypeArities(arities, type);
            return;
        }
        string schemaId = type.DefinitionId!;
        if (families.TryGetValue(schemaId, out SchemaKind previous) && previous != kind) {
            throw new InvalidDataException($"Schema family '{schemaId}' cannot change kind across versions.");
        }
        families[schemaId] = kind;
        ValidateTypeArities(arities, type);
    }

    internal static void ValidateTypeArities(Dictionary<string, int> arities, TypeExpr type) {
        if (type.IsArray) {
            ValidateTypeArities(arities, type.ElementType!);
            return;
        }
        if (type.Kind != TypeExprKind.Named) { return; }
        string id = type.DefinitionId!;
        if (arities.TryGetValue(id, out int arity) && arity != type.Arguments.Length) {
            throw new InvalidDataException($"Schema definition '{id}' cannot change generic arity.");
        }
        arities[id] = type.Arguments.Length;
        foreach (TypeExpr argument in type.Arguments) { ValidateTypeArities(arities, argument); }
    }

    internal static SchemaKey Key(DurableSchema schema) => new(schema.Type, schema.Version);

    private static SchemaKey ReadKey(ref BinaryPayloadReader reader, byte version) =>
        version < 3 ? SchemaKeyWireCodec.ReadLegacy(ref reader) : SchemaKeyWireCodec.Read(ref reader, allowArrays: version >= 4);

    private static int Compare(SchemaKey left, SchemaKey right) {
        int identity = left.Type.CompareTo(right.Type);
        return identity == 0 ? left.Version.CompareTo(right.Version) : identity;
    }

    private static byte EncodeType(TypeTag tag) => tag switch {
        TypeTag.Boolean => 1, TypeTag.Int32 => 2, TypeTag.Int64 => 3,
        TypeTag.String => 4, TypeTag.Byte => 5, TypeTag.SByte => 6,
        TypeTag.Int16 => 7, TypeTag.UInt16 => 8, TypeTag.UInt32 => 9,
        TypeTag.UInt64 => 10, TypeTag.Char => 11, TypeTag.Half => 12,
        TypeTag.Single => 13, TypeTag.Double => 14,
        TypeTag.ObjectReference => 15, TypeTag.InlineValue => 16,
        _ => throw new ArgumentException("Unsupported Schema field type.", nameof(tag)),
    };

    private static TypeTag DecodeType(byte tag) => tag switch {
        1 => TypeTag.Boolean, 2 => TypeTag.Int32, 3 => TypeTag.Int64,
        4 => TypeTag.String, 5 => TypeTag.Byte, 6 => TypeTag.SByte,
        7 => TypeTag.Int16, 8 => TypeTag.UInt16, 9 => TypeTag.UInt32,
        10 => TypeTag.UInt64, 11 => TypeTag.Char, 12 => TypeTag.Half,
        13 => TypeTag.Single, 14 => TypeTag.Double,
        15 => TypeTag.ObjectReference, 16 => TypeTag.InlineValue,
        _ => throw new InvalidDataException($"Unknown Schema field type code {tag}."),
    };

    private sealed record Declaration(SchemaKind Kind, SchemaKey? BaseKey, FieldDeclaration[] Fields);
    private sealed record FieldDeclaration(int FieldId, TypeTag Tag, TypeExpr? TargetType, SchemaKey? InlineKey);

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
