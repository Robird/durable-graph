using System.Security.Cryptography;
using System.Text;

namespace Atelia.ListDeltaReplayProbe;

internal sealed record Edit(int Round, string Kind, int TestId, int Ratio);

internal static class Scripts {
    internal static Edit[] Create(int seed, int rounds) {
        Random random = new(seed);
        List<Edit> edits = [];
        for (int round = 0; round < rounds; round++) {
            int id = -1000 - round * 100;
            foreach (string kind in new[] { "HeadInsert", "MiddleInsert", "HeadDelete", "MiddleDelete", "TailAppend", "TailDelete",
                "ScatterInsert", "ScatterDelete", "SparseReplace", "Rotate", "Reverse", "AllMicroThenInsert", "Undo", "ChildOnly" }) {
                edits.Add(new(round, kind, id, random.Next(1000, 9000)));
            }
        }
        return edits.ToArray();
    }
}

internal sealed class Workload<T>(string name, Func<int, Node[], T> create, Func<T, int> id,
    Func<T, T> change, Func<T, string> describe, bool referenceElements = false) {
    internal string Name => name;

    internal World<T> Seed(int count) {
        Node[] pool = referenceElements ? Enumerable.Range(0, 8).Select(index => new Node { TestId = index, Value = index * 100 }).ToArray() : [];
        for (int index = 0; index < pool.Length; index++) { pool[index].Next = pool[(index + 1) % pool.Length]; }
        List<T> items = Enumerable.Range(0, count).Select(index => create(index, pool)).ToList();
        return new() { Items = items, Alias = items, Pool = pool };
    }

    internal void Apply(World<T> world, Edit edit) {
        List<T> items = world.Items;
        T New(int suffix) => create(edit.TestId - suffix, world.Pool);
        int At(int ratio) => (int)((long)items.Count * ratio / 10000);
        // Tests use positions or stable domain test IDs; repository-local ObjectIds never address edits.
        void Remove(int suffix) {
            if (referenceElements) { items.RemoveAt(Math.Clamp(At(edit.Ratio), 0, items.Count - 1)); return; }
            int index = items.FindIndex(value => id(value) == edit.TestId - suffix);
            if (index < 0) { throw new InvalidOperationException("Script's stable test ID was not found."); }
            items.RemoveAt(index);
        }
        switch (edit.Kind) {
            case "HeadInsert": items.Insert(0, New(1)); break;
            case "MiddleInsert": items.Insert(items.Count / 2, New(2)); break;
            case "HeadDelete": if (referenceElements) { items.RemoveAt(0); } else { Remove(1); } break;
            case "MiddleDelete": Remove(2); break;
            case "TailAppend": items.Add(New(3)); break;
            case "TailDelete": items.RemoveAt(items.Count - 1); break;
            case "ScatterInsert":
                items.Insert(items.Count / 4, New(4)); items.Insert(items.Count / 2, New(5)); items.Insert(items.Count * 3 / 4, New(6)); break;
            case "ScatterDelete": Remove(4); Remove(5); Remove(6); break;
            case "SparseReplace":
                items[At(edit.Ratio)] = change(items[At(edit.Ratio)]);
                items[items.Count / 3] = change(items[items.Count / 3]); break;
            case "Rotate":
                int split = Math.Max(1, items.Count / 4);
                T[] prefix = items.Take(split).ToArray(); items.RemoveRange(0, split); items.AddRange(prefix); break;
            case "Reverse": items.Reverse(); break;
            case "AllMicroThenInsert":
                for (int index = 0; index < items.Count; index++) { items[index] = change(items[index]); }
                items.Insert(items.Count / 2, New(7)); break;
            case "Undo": items.Insert(0, New(8)); items.RemoveAt(0); break;
            case "ChildOnly": if (world.Pool.Length != 0) { world.Pool[1].Value++; } break;
            default: throw new InvalidOperationException("Unknown edit.");
        }
    }

    internal string Fingerprint(World<T> world) {
        VerifyIdentity(world);
        string values = string.Join("|", world.Items.Select(describe));
        string children = string.Join("|", world.Pool.Select(node => $"{node.TestId}:{node.Value}:{node.Next!.TestId}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values + "#" + children)));
    }

    private void VerifyIdentity(World<T> world) {
        if (!ReferenceEquals(world.Items, world.Alias)) { throw new InvalidOperationException("Shared List identity was lost."); }
        for (int index = 0; index < world.Pool.Length; index++) {
            if (!ReferenceEquals(world.Pool[index].Next, world.Pool[(index + 1) % world.Pool.Length])) {
                throw new InvalidOperationException("Child cycle was lost.");
            }
        }
        if (referenceElements) {
            foreach (T item in world.Items) {
                if (item is Node node && !ReferenceEquals(node, world.Pool[node.TestId])) {
                    throw new InvalidOperationException("Repeated child identity was lost.");
                }
            }
        }
    }
}

internal static class Workloads {
    internal static readonly Workload<int> Integers = new("int-unique", (id, _) => id, value => value, value => value + 10_000_000, value => value.ToString());
    internal static readonly Workload<int> Alternating = new("int-alternating", (id, _) => id < 0 ? id : id % 2, value => value,
        value => value + 10_000_000, value => value.ToString());
    internal static readonly Workload<Node?> References = new("object-id-duplicates-null", (id, nodes) => Math.Abs(id) % 4 == 0 ? null : nodes[Math.Abs(id) % nodes.Length],
        node => node?.TestId ?? int.MinValue, node => node, node => node is null ? "null" : $"{node.TestId}:{node.Value}", referenceElements: true);
    internal static readonly Workload<Cell<int>> Small = new("small-generic-inline", (id, _) => new() { TestId = id, Value = id * 17 },
        value => value.TestId, value => { value.Value++; return value; }, value => $"{value.TestId}:{value.Value}");
    internal static readonly Workload<Wide<Cell<int>>> Large = new("wide-nested-generic-inline", (id, _) => new() {
        TestId = id, Value = new() { TestId = id, Value = id * 17 },
        A = 1_000_000_000L + id, B = 2_000_000_000L + id, C = 3_000_000_000L + id, D = 4_000_000_000L + id,
        E = 5_000_000_000L + id, F = 6_000_000_000L + id, G = 7_000_000_000L + id, H = 8_000_000_000L + id,
    }, value => value.TestId, value => { value.Value.Value++; return value; },
        value => $"{value.TestId}:{value.Value.TestId}:{value.Value.Value}:{value.A}:{value.B}:{value.C}:{value.D}:{value.E}:{value.F}:{value.G}:{value.H}");
}
