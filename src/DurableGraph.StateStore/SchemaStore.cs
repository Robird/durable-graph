using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Persists exact Schemas and complete object representation IDs in one append-only RBF file.</summary>
/// <remarks>
/// The caller owns the file and its exclusive writer lifetime. Do not append to or
/// truncate it outside this store, or share it with another live SchemaStore.
/// A Schema batch or representation batch is one frame. Registration returns after DurableFlush, independently of
/// State publication. An append or flush failure faults this instance until reopen.
/// Writable reopen confirms recovered nonempty content with a DurableFlush before
/// returning. Read-only recovery validates observable bytes without confirming a
/// new durability barrier, and cannot be used to register schemas for State saves.
/// Recovery validates every physical frame and never truncates or skips bad bytes,
/// including an incomplete tail. Tombstones are not legal registration records.
/// This MVP registry is repository-wide and monotonic; future joint Commit/Ref
/// views may version its visibility without changing exact definition semantics.
/// </remarks>
public sealed class SchemaStore {
    private readonly IRbfFile _file;
    private readonly bool _readOnly;
    private Dictionary<SchemaKey, DurableSchema> _schemas = new();
    private Dictionary<RepresentationId, ObjectLayout> _representations = new() { [RepresentationId.String] = ObjectLayout.String };
    private Dictionary<ObjectLayout, RepresentationId> _representationIds = new() { [ObjectLayout.String] = RepresentationId.String };
    private ulong _nextRepresentationId = 2;
    private bool _busy;
    private long _acceptedTail;

    public SchemaStore(IRbfFile file, bool readOnly = false) {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
        _readOnly = readOnly;
        var frames = file.ScanForward(showTombstone: true).GetEnumerator();
        bool hadFrames = false;
        while (frames.MoveNext()) {
            hadFrames = true;
            using RbfPooledFrame frame = file.ReadPooledFrame(frames.Current.Ticket).Unwrap();
            if (frame.IsTombstone || frame.TailMetaLength != 0) {
                throw new InvalidDataException("Unexpected frame in the dedicated SchemaStore file.");
            }
            if (frame.Tag == SchemaBatchWireCodec.RbfTag) {
                Dictionary<SchemaKey, DurableSchema> merged = SchemaBatchWireCodec.Read(frame.PayloadAndMeta, _schemas);
                ValidateDeclarations(merged.Values, _representations.Values);
                _schemas = merged;
            }
            else if (frame.Tag == RepresentationBatchWireCodec.RbfTag) {
                RecoverRepresentations(frame.PayloadAndMeta);
            }
            else { throw new InvalidDataException("Unexpected frame in the dedicated SchemaStore file."); }
        }
        if (frames.TerminationError is { } error) {
            throw new InvalidDataException($"SchemaStore framing is invalid; no automatic tail recovery is performed. {error.Message}");
        }
        // A previous caller may have observed an uncertain flush outcome. Merely
        // reading complete bytes from the OS cache is not a new durable barrier.
        if (!readOnly && hadFrames) { file.DurableFlush(); }
        _acceptedTail = file.TailOffset;
    }

    /// <summary>The number of registered user Schema definitions, excluding representation IDs and built-ins.</summary>
    public int Count {
        get {
            RequireAvailable();
            return _schemas.Count;
        }
    }

    public bool IsFaulted { get; private set; }

    public DurableSchema Register(DurableSchema schema) {
        RegisterBatch(new[] { schema });
        return GetRequired(SchemaBatchWireCodec.Key(schema));
    }

    public void RegisterBatch(IEnumerable<DurableSchema> schemas) {
        RequireAvailable();
        if (_readOnly) { throw new InvalidOperationException("This SchemaStore is read-only."); }
        ArgumentNullException.ThrowIfNull(schemas);
        _busy = true;
        try {
            SchemaRegistration prepared = PrepareSchemas(schemas, []);
            RequireUnchangedTail();
            if (prepared.Payload is { } payload) {
                AppendDurably(SchemaBatchWireCodec.RbfTag, payload);
                _schemas = prepared.Schemas;
            }
        }
        finally {
            _busy = false;
        }
    }

    /// <summary>Registers complete layouts and returns IDs in input order after their durable barrier.</summary>
    /// <remarks>
    /// Exact Schema dependencies are validated even for an existing ID. Missing Schemas
    /// become durable before representation rows. A failed second append may leave those
    /// Schemas registered; any uncertain write faults this store until it is reopened.
    /// </remarks>
    public RepresentationId[] RegisterRepresentations(IReadOnlyList<ObjectLayout> layouts) {
        RequireAvailable();
        if (_readOnly) { throw new InvalidOperationException("This SchemaStore is read-only."); }
        ArgumentNullException.ThrowIfNull(layouts);
        _busy = true;
        try {
            var frozen = new ObjectLayout[layouts.Count];
            for (int i = 0; i < frozen.Length; i++) {
                frozen[i] = layouts[i];
                ArgumentNullException.ThrowIfNull(frozen[i]);
            }
            DurableSchema[] roots = frozen.Select(static layout => layout.Schema ?? layout.Array?.ElementSlot.InlineSchema)
                .OfType<DurableSchema>().ToArray();
            SchemaRegistration prepared = PrepareSchemas(roots, frozen);
            var ids = new Dictionary<ObjectLayout, RepresentationId>(_representationIds);
            var representations = new Dictionary<RepresentationId, ObjectLayout>(_representations);
            var additions = new List<KeyValuePair<RepresentationId, ObjectLayout>>();
            var result = new RepresentationId[frozen.Length];
            ulong next = _nextRepresentationId;
            for (int i = 0; i < frozen.Length; i++) {
                ObjectLayout layout = frozen[i];
                if (!ids.TryGetValue(layout, out RepresentationId id)) {
                    if (next > uint.MaxValue) { throw new InvalidOperationException("Repository representation IDs are exhausted."); }
                    id = new((uint)next++);
                    ids.Add(layout, id);
                    representations.Add(id, layout);
                    additions.Add(new(id, layout));
                }
                result[i] = id;
            }
            // Both frames are completely prepared before the first append. In particular,
            // late descriptor/capacity failures cannot partially register an input batch.
            byte[]? representationPayload = additions.Count == 0 ? null : RepresentationBatchWireCodec.Write(additions);
            RequireUnchangedTail();
            if (prepared.Payload is { } schemaPayload) {
                AppendDurably(SchemaBatchWireCodec.RbfTag, schemaPayload);
                _schemas = prepared.Schemas;
            }
            if (representationPayload is not null) {
                AppendDurably(RepresentationBatchWireCodec.RbfTag, representationPayload);
                _representations = representations;
                _representationIds = ids;
                _nextRepresentationId = next;
            }
            return result;
        }
        finally { _busy = false; }
    }

    /// <summary>Resolves a persisted ID without registering or inferring a replacement layout.</summary>
    public ObjectLayout GetRepresentation(RepresentationId id) {
        RequireAvailable();
        return _representations.TryGetValue(id, out ObjectLayout? layout) ? layout
            : throw new InvalidDataException($"Unknown repository representation ID {id.Value}.");
    }

    /// <summary>Binds the exact stored representation using this operation's frozen code catalog.</summary>
    /// <remarks>Only descriptors persist. CLR types/readers are resolved per supplied catalog, never cached globally here.</remarks>
    public ObjectReaderBinding ResolveReader(RepresentationId id, StateBindingContext bindings) {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectLayout layout = GetRepresentation(id);
        ObjectReaderBinding reader = bindings.ResolveObjectReader(layout);
        if (reader is null || !layout.Equals(reader.Layout)) {
            throw new InvalidDataException($"Reader binding does not match representation ID {id.Value}.");
        }
        return reader;
    }

    public DurableSchema GetRequired(string schemaId, int version) => GetRequired(new SchemaKey(schemaId, version));

    public DurableSchema GetRequired(TypeExpr type, int version) => GetRequired(new SchemaKey(type, version));

    /// <summary>Looks up an already registered exact layout without changing registry visibility.</summary>
    public bool TryGet(SchemaKey key, out DurableSchema? schema) {
        RequireAvailable();
        key.Validate();
        return _schemas.TryGetValue(key, out schema);
    }

    public DurableSchema GetRequired(SchemaKey key) {
        RequireAvailable();
        key.Validate();
        return _schemas.TryGetValue(key, out DurableSchema? schema)
            ? schema
            : throw new SchemaNotFoundException(key.SchemaId, key.Version);
    }

    private SchemaRegistration PrepareSchemas(IEnumerable<DurableSchema> roots, IReadOnlyList<ObjectLayout> additionalLayouts) {
        var merged = new Dictionary<SchemaKey, DurableSchema>(_schemas);
        var heights = new Dictionary<DurableSchema, int>(ReferenceEqualityComparer.Instance);
        foreach (DurableSchema schema in roots) {
            ArgumentNullException.ThrowIfNull(schema);
            AddClosure(schema, 1);
        }
        try { ValidateDeclarations(merged.Values, _representations.Values.Concat(additionalLayouts)); }
        catch (InvalidDataException error) { throw new ArgumentException(error.Message, nameof(roots), error); }
        DurableSchema[] missing = merged.Where(pair => !_schemas.ContainsKey(pair.Key)).Select(static pair => pair.Value).ToArray();
        return new(merged, missing.Length == 0 ? null : SchemaBatchWireCodec.Write(missing));

        int AddClosure(DurableSchema schema, int depth) {
            if (depth > SchemaBatchWireCodec.MaximumDepth) {
                throw new ArgumentException("Schema layout exceeds the maximum depth.", nameof(roots));
            }
            if (heights.TryGetValue(schema, out int cached)) {
                if (depth + cached - 1 > SchemaBatchWireCodec.MaximumDepth) {
                    throw new ArgumentException("Schema layout exceeds the maximum depth.", nameof(roots));
                }
                return cached;
            }
            int height = 1;
            if (schema.BaseSchema is { } ancestor) { height = Math.Max(height, 1 + AddClosure(ancestor, depth + 1)); }
            foreach (DurableFieldInfo field in schema.Fields) {
                if (field.InlineSchema is { } inline) { height = Math.Max(height, 1 + AddClosure(inline, depth + 1)); }
            }
            SchemaKey key = SchemaBatchWireCodec.Key(schema);
            if (merged.TryGetValue(key, out DurableSchema? old)) {
                if (!old.Equals(schema)) { throw new SchemaConflictException(old, schema); }
            }
            else { merged.Add(key, schema); }
            heights.Add(schema, height);
            return height;
        }
    }

    private static void ValidateDeclarations(IEnumerable<DurableSchema> schemas, IEnumerable<ObjectLayout> layouts) {
        var familyKinds = new Dictionary<string, SchemaKind>(StringComparer.Ordinal);
        var familyArities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DurableSchema schema in schemas) {
            SchemaBatchWireCodec.ValidateDeclaration(familyKinds, familyArities, schema.Type, schema.Kind);
            foreach (DurableFieldInfo field in schema.Fields) { ValidateReference(field); }
        }
        foreach (ObjectLayout layout in layouts) {
            if (layout.Array is { } array) { ValidateReference(array.ElementSlot); }
        }
        void ValidateReference(DurableFieldInfo field) {
            if (field.TargetType is { } target) {
                SchemaBatchWireCodec.ValidateDeclaration(familyKinds, familyArities, target, SchemaKind.ReferenceObject);
            }
        }
    }

    private void RecoverRepresentations(ReadOnlySpan<byte> payload) {
        KeyValuePair<RepresentationId, ObjectLayout>[] rows = RepresentationBatchWireCodec.Read(payload, key =>
            _schemas.TryGetValue(key, out DurableSchema? schema) ? schema
                : throw new InvalidDataException($"Missing exact Schema '{key.Type}' version {key.Version} for representation."));
        var representations = new Dictionary<RepresentationId, ObjectLayout>(_representations);
        var ids = new Dictionary<ObjectLayout, RepresentationId>(_representationIds);
        ulong next = _nextRepresentationId;
        foreach ((RepresentationId id, ObjectLayout layout) in rows) {
            if (id.Value != next || !representations.TryAdd(id, layout) || !ids.TryAdd(layout, id)) {
                throw new InvalidDataException("Representation registration contains a gap, repeated ID, or duplicate layout.");
            }
            next++;
        }
        ValidateDeclarations(_schemas.Values, representations.Values);
        _representations = representations;
        _representationIds = ids;
        _nextRepresentationId = next;
    }

    private void AppendDurably(uint tag, byte[] payload) {
        try {
            _file.Append(tag, payload).Unwrap();
            _file.DurableFlush();
            _acceptedTail = _file.TailOffset;
        }
        catch { IsFaulted = true; throw; }
    }

    private sealed record SchemaRegistration(Dictionary<SchemaKey, DurableSchema> Schemas, byte[]? Payload);

    private void RequireAvailable() {
        if (IsFaulted) { throw new InvalidOperationException("Schema registration outcome is uncertain; reopen the SchemaStore before further use."); }
        if (_busy) { throw new InvalidOperationException("SchemaStore operations cannot be reentered."); }
        RequireUnchangedTail();
    }

    private void RequireUnchangedTail() {
        if (_file.TailOffset != _acceptedTail) {
            IsFaulted = true;
            throw new InvalidOperationException("The SchemaStore file was changed outside its owning store; reopen before further use.");
        }
    }
}
