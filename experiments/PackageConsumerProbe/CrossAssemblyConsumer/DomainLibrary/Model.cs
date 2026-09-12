using Atelia.DurableGraph;
using Atelia.DurableGraph.Runtime;
using NodeStates = Atelia.DurableGraph.Generated.Family_4E6F6465;
#if HISTORY_V1
using Payload = CrossAssembly.Domain.LegacyPayload;
#else
using Payload = CrossAssembly.Domain.CurrentPayload;
#endif

namespace CrossAssembly.Domain;

// Deliberately ordinary structs/classes: generation V1 has no automatic Family trigger.
#if HISTORY_V1
[DurableType("Payload", 1)]
public partial struct LegacyPayload {
    [DurableField(1)] public int Count;
}
#else
[DurableType("Payload", 2)]
public partial struct CurrentPayload {
    [DurableField(1)] public long Count;
}
#endif

#if HISTORY_V1
[DurableType("Node", 1)]
#else
[DurableType("Node", 2)]
#endif
public partial class Node : IDurableObject {
    [DurableField(1)] private Payload _payload;
    [DurableField(2)] private Guid _token;
    [DurableField(3)] public Node? Next;

    // This public API and its CLR signatures stay unchanged across both library packages.
    public long Value {
        get => _payload.Count;
        set {
#if HISTORY_V1
            _payload.Count = checked((int)value);
#else
            _payload.Count = value;
#endif
        }
    }
    public static Node Create(long value) {
        var result = new Node { Value = value, _token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff") };
        result.Next = result;
        return result;
    }
}

public static class DomainCatalog {
    public static void Register(IStateModelRegistration models) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
    public static void RegisterReaders(IStateReaderRegistration readers) => Atelia.DurableGraph.Generated.DurableDefinitions.Register(readers);
    public static int UpgradeCalls { get; private set; }
    public static bool LegacyClrAbsent => typeof(Node).Assembly.GetType("CrossAssembly.Domain.LegacyPayload") is null;

    // Tests stored DTOs in their owning library without exporting generated DTO names into AppModel/Host.
    public static void CheckHistorical(ObjectStateRecord record, long expected) {
        var old = record.GetState<NodeStates.V1>();
        if (old.Segment0Field1.Segment0Field1 != expected || old.Segment0Field3 != record.Id) {
            throw new InvalidOperationException("Historical inline DTO or self-reference was not preserved.");
        }
    }

#if HISTORY_V2
    [DurableUpgrade(typeof(Node), 1)]
    internal static void Upgrade(in NodeStates.V1 old, out NodeStates.V2 next, UpgradeContext context) {
        UpgradeCalls++;
        next = new(new(old.Segment0Field1.Segment0Field1 + 1000L), old.Segment0Field2, old.Segment0Field3);
    }
#endif
}
