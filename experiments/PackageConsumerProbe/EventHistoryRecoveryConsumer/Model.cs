using Atelia.DurableGraph;

namespace EventHistoryRecovery;

[DurableType("RecoveryCharacter", 1)]
public sealed partial class Character : IDurableObject {
    [DurableField(1)] public string ActorId = "hero";
    [DurableField(2)] public int Hp = 10;
    [DurableField(3)] public List<string> Observations = ["ready", "armed"];
}

[DurableType("RecoveryWorld", 1)]
public sealed partial class World : IDurableObject {
    [DurableField(1)] public Character Actor = new();
    [Transient] private Dictionary<string, Character>? _actors;
    public void RebuildTransient() => _actors = new() { [Actor.ActorId] = Actor };
    public Character Find(string id) => (_actors ?? throw new InvalidOperationException("Rebuild Transient first."))[id];
}

// An application domain snapshot, not the framework-generated Versioned DTO.
[DurableType("RecoveryActorSnapshot", 1)]
public sealed partial class ActorSnapshot : IDurableObject {
    [DurableField(1)] public readonly string ActorId;
    [DurableField(2)] public readonly int Hp;
    [DurableField(3)] private readonly string[] _observations;
    public ActorSnapshot(Character actor) {
        ActorId = actor.ActorId;
        Hp = actor.Hp;
        _observations = actor.Observations.ToArray();
    }
    public int ObservationCount => _observations.Length;
    public string GetObservation(int index) => _observations[index];
}

[DurableType("RecoveryDamageEvent", 1)]
public sealed partial class DamageEvent : IDurableObject {
    [DurableField(1)] public readonly ActorSnapshot TargetSnapshot;
    [DurableField(2)] public readonly int Amount;
    public DamageEvent(Character target, int amount) {
        TargetSnapshot = new(target);
        Amount = amount;
    }
}
