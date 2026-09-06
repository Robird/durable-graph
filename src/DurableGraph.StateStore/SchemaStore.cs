using Atelia.Rbf;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Persists exact schema definitions in a dedicated, append-only RBF file.</summary>
/// <remarks>
/// The caller owns the file and its exclusive writer lifetime. Do not append to or
/// truncate it outside this store, or share it with another live SchemaStore.
/// A batch is one frame. Registration returns after DurableFlush, independently of
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
    private bool _busy;
    private long _acceptedTail;

    public SchemaStore(IRbfFile file, bool readOnly = false) {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
        _readOnly = readOnly;
        var frames = file.ScanForward(showTombstone: true).GetEnumerator();
        while (frames.MoveNext()) {
            using RbfPooledFrame frame = file.ReadPooledFrame(frames.Current.Ticket).Unwrap();
            if (frame.IsTombstone || frame.Tag != SchemaBatchWireCodec.RbfTag || frame.TailMetaLength != 0) {
                throw new InvalidDataException("Unexpected frame in the dedicated SchemaStore file.");
            }
            _schemas = SchemaBatchWireCodec.Read(frame.PayloadAndMeta, _schemas);
        }
        if (frames.TerminationError is { } error) {
            throw new InvalidDataException($"SchemaStore framing is invalid; no automatic tail recovery is performed. {error.Message}");
        }
        // A previous caller may have observed an uncertain flush outcome. Merely
        // reading complete bytes from the OS cache is not a new durable barrier.
        if (!readOnly && _schemas.Count != 0) { file.DurableFlush(); }
        _acceptedTail = file.TailOffset;
    }

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
            var merged = new Dictionary<SchemaKey, DurableSchema>(_schemas);
            foreach (DurableSchema schema in schemas) {
                ArgumentNullException.ThrowIfNull(schema);
                int depth = 0;
                // Check depth before structural equality, whose recursion follows the chain.
                for (DurableSchema? item = schema; item is not null; item = item.BaseSchema) {
                    if (++depth > SchemaBatchWireCodec.MaximumDepth) {
                        throw new ArgumentException("Schema inheritance exceeds the maximum depth.", nameof(schemas));
                    }
                }
                for (DurableSchema? item = schema; item is not null; item = item.BaseSchema) {
                    SchemaKey key = SchemaBatchWireCodec.Key(item);
                    if (merged.TryGetValue(key, out DurableSchema? old)) {
                        if (!old.Equals(item)) { throw new SchemaConflictException(old, item); }
                    }
                    else {
                        merged.Add(key, item);
                    }
                }
            }
            DurableSchema[] missing = merged.Where(pair => !_schemas.ContainsKey(pair.Key)).Select(static pair => pair.Value).ToArray();
            RequireUnchangedTail();
            if (missing.Length == 0) { return; }
            byte[] payload = SchemaBatchWireCodec.Write(missing);
            try {
                _file.Append(SchemaBatchWireCodec.RbfTag, payload).Unwrap();
                _file.DurableFlush();
                _acceptedTail = _file.TailOffset;
                _schemas = merged;
            }
            catch {
                IsFaulted = true;
                throw;
            }
        }
        finally {
            _busy = false;
        }
    }

    public DurableSchema GetRequired(string schemaId, int version) => GetRequired(new SchemaKey(schemaId, version));

    public DurableSchema GetRequired(SchemaKey key) {
        RequireAvailable();
        key.Validate();
        return _schemas.TryGetValue(key, out DurableSchema? schema)
            ? schema
            : throw new SchemaNotFoundException(key.SchemaId, key.Version);
    }

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
