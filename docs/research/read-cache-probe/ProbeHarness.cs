using System.Diagnostics;
using System.Text.Json;
using System.Collections;
using System.Collections.ObjectModel;
using Atelia.DurableGraph.StateStore.Storage;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

// Research ablation: maps-only / frames+maps use unbounded operation-local maps;
// unified modes instead share one bounded, cross-operation LRU for both values.
// The frame cache omits the production lifetime guard, which must be owned by GraphResources.
// Neither prototype is a proposed public API or a product implementation.
var cases = new[] {
    new Case("tiny", 1, 0, 32, false),
    new Case("wide-base", 256, 0, 128, false),
    new Case("sparse-history", 128, 24, 128, true),
    new Case("dense-deltas", 64, 12, 64, false),
    new Case("oversized-frame", 64, 0, 4096, false),
    new Case("map-heavy", 128, 32, 0, true)
};
var modes = new[] {
    new Mode("direct", 0, false, RbfCacheMode.Slots16),
    new Mode("page64", 0, false, RbfCacheMode.Slots64),
    new Mode("frames-8MiB", 8L * 1024 * 1024, false, RbfCacheMode.Slots16),
    new Mode("maps-only", 0, true, RbfCacheMode.Slots16),
    new Mode("frames+maps", 8L * 1024 * 1024, true, RbfCacheMode.Slots16),
    new Mode("frames-16KiB", 16L * 1024, false, RbfCacheMode.Slots16),
    new Mode("unified-8MiB", 8L * 1024 * 1024, true, RbfCacheMode.Slots16, true),
    new Mode("unified-16KiB", 16L * 1024, true, RbfCacheMode.Slots16, true),
    new Mode("frames-64KiB", 64L * 1024, false, RbfCacheMode.Slots16),
    new Mode("unified-64KiB", 64L * 1024, true, RbfCacheMode.Slots16, true)
};
var ownership = CheckOwnership();
Console.WriteLine(JsonSerializer.Serialize(ownership));
List<object> output = [];
foreach (Case fixture in cases) {
    string path = Path.Combine(Path.GetTempPath(), "dg-read-cache-data-" + Guid.NewGuid().ToString("N"));
    FrameAddress head;
    using (SegmentStore segments = SegmentStore.CreateNew(path)) {
        StateRevisionStore store = new(segments);
        head = store.Append(StateRevision.CreateObjectHeadMapBase(null,
            Enumerable.Range(1, fixture.Objects).Select(id => ObjectVersionRecord.CreateBase((uint)id, Body(fixture.BodyBytes, id))), []));
        Dictionary<uint, FrameAddress> heads = Enumerable.Range(1, fixture.Objects).ToDictionary(id => (uint)id, _ => head);
        for (int step = 0; step < fixture.Depth; step++) {
            uint[] ids = fixture.Sparse ? [(uint)(step % fixture.Objects + 1)] : heads.Keys.ToArray();
            StateRevision revision = StateRevision.CreateObjectHeadMapDelta(head,
                ids.Select(id => ObjectVersionRecord.CreateDelta(id, heads[id], Body(fixture.BodyBytes, step + (int)id))), []);
            head = store.Append(revision);
            foreach (uint id in ids) { heads[id] = head; }
        }
    }
    long? expected = null;
    // Alternate ordering by fixture; warm-up is separate from each measured operation.
    foreach (Mode mode in fixture.Objects % 3 == 0 ? modes.Reverse() : modes) {
        using SegmentStore segments = SegmentStore.OpenReadOnlyExisting(path, new() { CacheMode = mode.Pages, RecoverActiveTailOnOpen = false });
        StateRevisionStore store = new(segments);
        store.Memo.Budget = mode.Budget;
        store.Memo.MapsEnabled = mode.Maps;
        store.Memo.UnifiedMaps = mode.Unified;
        store.Memo.Reset(clearFrames: true);
        _ = ReadAll(store, head);
        List<Sample> cold = [];
        List<Sample> warm = [];
        for (int repeat = 0; repeat < 3; repeat++) {
            cold.Add(Measure(store, head, true));
            warm.Add(Measure(store, head, false));
        }
        foreach (Sample sample in cold.Concat(warm)) {
            expected ??= sample.Checksum;
            if (sample.Checksum != expected.Value) { throw new Exception("Ablation changed raw content or H."); }
        }
        var result = new { fixture = fixture.Name, fixture.Objects, fixture.Depth, fixture.BodyBytes,
            mode = mode.Name, cold = Median(cold), warm = Median(warm), coldSamples = cold, warmSamples = warm };
        output.Add(result);
        Console.WriteLine($"{fixture.Name,-18} {mode.Name,-16} cold={Median(cold).Milliseconds:F3}ms; decodes={Median(cold).WireDecodes}; maps={Median(cold).MapBuilds}; allocated={Median(cold).AllocatedBytes}");
    }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new {
    runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), processorCount = Environment.ProcessorCount,
    repetitions = 3, tieredCompilation = false, ownership,
    note = "Cold means empty decoded cache; OS pages are warm. All variants use the ownership-hardened map wrapper. Storage read-loop ablation, not full Commit/Load or physical I/O measurement.",
    results = output
}, new JsonSerializerOptions { WriteIndented = true }));

static byte[] Body(int length, int seed) {
    byte[] body = new byte[length];
    for (int i = 0; i < body.Length; i++) { body[i] = (byte)(seed + i); }
    return body;
}

static object CheckOwnership() {
    SortedDictionary<uint, FrameAddress> data = new() { [1] = default };
    ReadOnlyDictionary<uint, FrameAddress> oldWrapper = new(data);
    object sync = ((ICollection)oldWrapper).SyncRoot;
    bool keysSame = ReferenceEquals(sync, ((ICollection)oldWrapper.Keys).SyncRoot);
    bool valuesSame = ReferenceEquals(sync, ((ICollection)oldWrapper.Values).SyncRoot);
    bool exposed = sync is ICollection<KeyValuePair<uint, FrameAddress>>;
    if (sync is ICollection<KeyValuePair<uint, FrameAddress>> mutable) { mutable.Clear(); }
    IReadOnlyDictionary<uint, FrameAddress> fixedWrapper = new ProbeFrozenMap(new() { [1] = default });
    bool hardened = fixedWrapper is not ICollection && fixedWrapper.Keys is not ICollection && fixedWrapper.Values is not ICollection;
    if (!exposed || oldWrapper.Count != 0 || !hardened) { throw new Exception("Ownership witness changed; review runtime/source assumptions."); }
    return new { runtime = Environment.Version.ToString(), syncType = sync.GetType().FullName, keysSame, valuesSame,
        mutableBackingExposed = exposed, originalMapCountAfterSyncRootClear = oldWrapper.Count, hardened };
}

static long ReadAll(StateRevisionStore store, FrameAddress head) {
    // Mirrors RevisionDecoder's current Storage calls, without DTO/Schema/Upgrade/Hydrate work.
    var heads = store.ReadLiveObjectHeadMap(head);
    long checksum = heads.Count;
    foreach (uint id in heads.Keys) {
        ObjectVersionChain chain = store.ReadObjectVersionChain(head, id);
        checksum += chain.ReconstructionPayloadBytes;
        foreach (var entry in chain.Records) {
            checksum += entry.Record.Body.Length + entry.Record.ObjectId;
            if (!entry.Record.Body.IsEmpty) { checksum += entry.Record.Body[0] + entry.Record.Body[^1]; }
        }
    }
    return checksum;
}

static Sample Measure(StateRevisionStore store, FrameAddress head, bool clearFrames) {
    store.Memo.Reset(clearFrames);
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    long checksum = ReadAll(store, head);
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var memo = store.Memo;
    return new(ms, bytes, memo.ReadCalls, memo.Requested.Count, memo.Decodes, memo.FrameBytes,
        memo.MapCalls, memo.MapBuilds, memo.ResidentCharge, memo.PeakCharge, memo.Evictions, memo.Oversized, checksum);
}

static Sample Median(List<Sample> samples) => samples.OrderBy(s => s.Milliseconds).ElementAt(samples.Count / 2);

record Case(string Name, int Objects, int Depth, int BodyBytes, bool Sparse);
record Mode(string Name, long Budget, bool Maps, RbfCacheMode Pages, bool Unified = false);
record Sample(double Milliseconds, long AllocatedBytes, long ReadCalls, int UniqueFrames, long WireDecodes,
    long RequestedFrameBytesOnMiss, long MapCalls, long MapBuilds, long ResidentCharge,
    long PeakCharge, long Evictions, long Oversized, long Checksum);

namespace Atelia.DurableGraph.StateStore.Storage {
    internal sealed class ProbeMemo {
        internal long Budget, ReadCalls, Decodes, FrameBytes, MapCalls, MapBuilds, ResidentCharge, PeakCharge, Evictions, Oversized;
        internal bool MapsEnabled, UnifiedMaps;
        internal readonly HashSet<FrameAddress> Requested = [];
        internal readonly Dictionary<FrameAddress, IReadOnlyDictionary<uint, FrameAddress>> Maps = [];
        private readonly Dictionary<FrameAddress, LinkedListNode<Entry>> _frames = [];
        private readonly LinkedList<Entry> _lru = [];

        internal bool TryFrame(FrameAddress address, out StateRevision? revision) {
            revision = null;
            if (Budget == 0 || !_frames.TryGetValue(address, out var node)) { return false; }
            if (node.Value.Revision is null) { return false; }
            Touch(node);
            revision = node.Value.Revision;
            return true;
        }

        internal void AddFrame(FrameAddress address, StateRevision revision) {
            if (Budget == 0) { return; }
            // Deliberately explicit accounting proxy, not an exact CLR heap-size assertion.
            long charge = 256L + 128L * revision.LocalObjects.Count + 64L * revision.ExternalObjectHeads.Count
                + 4L * revision.RemovedObjectIds.Count;
            foreach (var record in revision.LocalObjects) { charge = checked(charge + record.Body.Length); }
            Admit(address, revision, null, charge);
        }

        internal bool TryMap(FrameAddress address, out IReadOnlyDictionary<uint, FrameAddress>? map) {
            map = null;
            if (!MapsEnabled) { return false; }
            if (!UnifiedMaps) { return Maps.TryGetValue(address, out map); }
            if (!_frames.TryGetValue(address, out var node) || node.Value.Map is null) { return false; }
            Touch(node);
            map = node.Value.Map;
            return true;
        }

        internal void AddMap(FrameAddress address, IReadOnlyDictionary<uint, FrameAddress> map) {
            if (!MapsEnabled) { return; }
            if (!UnifiedMaps) { Maps.Add(address, map); return; }
            // Proxy includes a fixed wrapper/container allowance and sorted-tree entries.
            Admit(address, null, map, checked(256L + 64L * map.Count));
        }

        private void Admit(FrameAddress address, StateRevision? revision, IReadOnlyDictionary<uint, FrameAddress>? map, long componentCharge) {
            _frames.TryGetValue(address, out var oldNode);
            long charge = checked(componentCharge + (oldNode?.Value.Charge ?? 0));
            if (charge > Budget) { Oversized++; return; }
            var entry = oldNode?.Value ?? new Entry(address);
            if (oldNode is not null) {
                ResidentCharge -= entry.Charge;
                _lru.Remove(oldNode);
                _frames.Remove(address);
            }
            while (ResidentCharge + charge > Budget) {
                var oldest = _lru.Last!;
                ResidentCharge -= oldest.Value.Charge;
                _frames.Remove(oldest.Value.Address);
                _lru.RemoveLast();
                Evictions++;
            }
            entry.Revision ??= revision;
            entry.Map ??= map;
            entry.Charge = charge;
            _frames.Add(address, _lru.AddFirst(entry));
            ResidentCharge += charge;
            PeakCharge = Math.Max(PeakCharge, ResidentCharge);
        }

        private void Touch(LinkedListNode<Entry> node) { _lru.Remove(node); _lru.AddFirst(node); }

        internal void Reset(bool clearFrames) {
            if (clearFrames) { _frames.Clear(); _lru.Clear(); ResidentCharge = 0; }
            Requested.Clear(); Maps.Clear();
            ReadCalls = Decodes = FrameBytes = MapCalls = MapBuilds = Evictions = Oversized = 0;
            PeakCharge = ResidentCharge;
        }

        private sealed class Entry(FrameAddress address) {
            internal FrameAddress Address { get; } = address;
            internal StateRevision? Revision;
            internal IReadOnlyDictionary<uint, FrameAddress>? Map;
            internal long Charge;
        }
    }

    // Same ownership shape already used privately by StateRevision. The producer owns data.
    internal sealed class ProbeFrozenMap(SortedDictionary<uint, FrameAddress> data) : IReadOnlyDictionary<uint, FrameAddress> {
        public int Count => data.Count;
        public FrameAddress this[uint key] => data[key];
        public bool ContainsKey(uint key) => data.ContainsKey(key);
        public bool TryGetValue(uint key, out FrameAddress value) => data.TryGetValue(key, out value);
        public IEnumerable<uint> Keys { get { foreach (var entry in data) { yield return entry.Key; } } }
        public IEnumerable<FrameAddress> Values { get { foreach (var entry in data) { yield return entry.Value; } } }
        public IEnumerator<KeyValuePair<uint, FrameAddress>> GetEnumerator() => data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
