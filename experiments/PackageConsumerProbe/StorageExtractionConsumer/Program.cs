using Atelia.DurableGraph;
using Atelia.DurableGraph.Persistence;
using Atelia.DurableGraph.Storage;
using Atelia.EventJournal;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using StorageExtractionConsumer;
using FrameAddress = Atelia.DurableGraph.Storage.FrameAddress;
using JournalStore = Atelia.EventJournal.EventJournal;
using SegmentStore = Atelia.RbfSegmentStore.RbfSegmentStore;

if (args.Length != 3) { throw new ArgumentException("Expected mode, database directory and evidence directory."); }
string mode = args[0], directory = Path.GetFullPath(args[1]), evidence = Path.GetFullPath(args[2]);
var options = new RbfSegmentStoreOptions { NewStoreLayout = RbfSegmentStoreLayout.Flat };
var models = new StateModelRegistry();
Atelia.DurableGraph.Generated.DurableDefinitions.Register(models);
foreach (string name in new[] {
    "Atelia.Primitives", "Atelia.Data", "Atelia.Rbf", "Atelia.RbfSegmentStore", "Atelia.EventJournal",
    "Atelia.DurableGraph.Serialization", "Atelia.DurableGraph", "Atelia.DurableGraph.Storage", "Atelia.DurableGraph.Persistence"
}) {
    Assembly assembly = Assembly.Load(name);
    Console.WriteLine("Loaded:" + JsonSerializer.Serialize(new {
        Name = name, assembly.FullName, assembly.Location,
        SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
    }));
}
var budget = new ReadAmplificationBaseBudgetParameters(int.MaxValue, 1);
switch (mode) {
    case "seed":
        using (var repository = EventHistoryRepository.CreateNew(directory, options)) {
            using (var session = repository.CreateBranch("complete", Make(100), models)) {
                session.CommitDomainEvent(Make(3));
                session.State.Value = 103;
                session.CommitDomainState(budget);
            }
            repository.CreateBranch("pending", repository.ReadFrames("complete").Last());
            using var pending = repository.Resume<World>("pending", models);
            pending.CommitDomainEvent(Make(7));
            Require(pending.PendingEvent is World { Value: 7 }, "Seed did not retain a pending Event.");
        }
        CheckDomain(false);
        Directory.CreateDirectory(evidence);
        WriteJson("old-branches.json", CaptureBranches());
        Inventory();
        break;
    case "read":
        CheckDomain(false);
        CheckOldBranches(false);
        break;
    case "continue":
        FrameAddress changed, unchanged;
        using (var repository = EventHistoryRepository.OpenExisting(directory, options)) {
            using var session = repository.Resume<World>("pending", models);
            Validate(session.State, 103);
            Require(session.PendingEvent is World { Value: 7 }, "Old pending Event missing.");
            Validate((World)session.PendingEvent!, 7);
            session.State.Value += ((World)session.PendingEvent!).Value;
            changed = session.CommitDomainState(budget).RevisionAddress;
            session.CommitDomainEvent(Make(0));
            unchanged = session.CommitDomainState(budget).RevisionAddress;
            Require(session.PendingEvent is null, "Completion retained PendingEvent.");
        }
        using (var segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(directory, "state"), options))
        using (var states = new StateRevisionStore(segments)) {
            Require(states.Read(changed).LocalObjects.Any(row => row.Kind == ObjectVersionKind.Delta), "Continuation did not produce Delta.");
            Require(states.Read(unchanged).LocalObjects.Count == 0, "Unchanged graph did not use NoChange.");
        }
        break;
    case "check":
        CheckDomain(true);
        CheckOldBranches(true);
        break;
    default:
        throw new ArgumentException("Unknown mode.");
}
Console.WriteLine($"StorageExtraction:{mode}:Passed");

void CheckDomain(bool continued) {
    using var repository = EventHistoryRepository.OpenReadOnlyExisting(directory, options);
    Require(repository.ListBranches().SequenceEqual(new[] { "complete", "pending" }), "Branch names changed.");
    var complete = repository.ReadFrames("complete");
    var pending = repository.ReadFrames("pending");
    Require(complete.Count == 3 && pending.Count == (continued ? 7 : 4), "Unexpected branch frame count.");
    Require(repository.GetHead("complete").Kind == GraphFrameKind.State, "Complete head changed role.");
    Require(repository.GetHead("pending").Kind == (continued ? GraphFrameKind.State : GraphFrameKind.Event), "Pending head changed role.");
    Validate(repository.ReadState<World>(complete[0], models), 100);
    Validate(repository.ReadEvent<World>(complete[1], models), 3);
    Validate(repository.ReadState<World>(complete[2], models), 103);
    Validate(repository.ReadEvent<World>(pending[3], models), 7);
    if (continued) {
        Validate(repository.ReadState<World>(pending[4], models), 110);
        Validate(repository.ReadEvent<World>(pending[5], models), 0);
        Validate(repository.ReadState<World>(pending[6], models), 110);
    }
    using var segments = SegmentStore.OpenReadOnlyExisting(Path.Combine(directory, "state"), options);
    using var states = new StateRevisionStore(segments);
    Require(states.Read(complete[0].RevisionAddress).LocalObjects.Any(row => row.Kind == ObjectVersionKind.Base), "Old Base missing.");
    Require(states.Read(complete[2].RevisionAddress).LocalObjects.Any(row => row.Kind == ObjectVersionKind.Delta), "Old Delta missing.");
}

BranchWitness[] CaptureBranches() {
    using var journal = JournalStore.OpenReadOnlyExisting(Path.Combine(directory, "journal"));
    using var repository = EventHistoryRepository.OpenReadOnlyExisting(directory, options);
    return journal.ListBranches().Select(name => {
        RefId refId = journal.OpenBranch(name).Unwrap();
        var chain = new List<string>();
        var visited = new HashSet<EventAddress>();
        EventAddress? cursor = journal.GetHead(refId);
        while (cursor is { } address) {
            Require(visited.Add(address), "Journal parent cycle.");
            using EventFrame frame = journal.ReadEvent(address).Unwrap();
            chain.Add($"{address.SegmentNumber}:{address.Ticket.Offset}:{address.Ticket.Length}:{address.Hint}");
            cursor = frame.Header.Parent;
        }
        chain.Reverse();
        string[] graphs = repository.ReadFrames(name)
            .Select(frame => $"{frame.Kind}:{frame.RevisionAddress}:{frame.RootId}").ToArray();
        Require(chain.Count == graphs.Length, "Journal parent chain differs from graph history.");
        return new BranchWitness(name, refId.ToHexString(), chain.ToArray(), graphs);
    }).ToArray();
}

void CheckOldBranches(bool continued) {
    BranchWitness[] expected = JsonSerializer.Deserialize<BranchWitness[]>(File.ReadAllText(Path.Combine(evidence, "old-branches.json")))!;
    BranchWitness[] actual = CaptureBranches();
    Require(expected.Length == actual.Length, "Ref set changed.");
    foreach (BranchWitness old in expected) {
        BranchWitness current = actual.Single(branch => branch.Name == old.Name);
        Require(current.RefId == old.RefId, "Persistent RefId changed.");
        Require(current.Events.Take(old.Events.Length).SequenceEqual(old.Events), "Old Event addresses, parent chain or head changed.");
        Require(current.Graphs.Take(old.Graphs.Length).SequenceEqual(old.Graphs), "Old graph role/root/revision changed.");
        int added = continued && old.Name == "pending" ? 3 : 0;
        Require(current.Events.Length == old.Events.Length + added, "Unexpected branch extension.");
    }
}

void Inventory() {
    var inventory = new List<object>();
    var categories = new HashSet<string>();
    foreach (string path in Directory.EnumerateFiles(directory, "*.rbf", SearchOption.AllDirectories).Order()) {
        string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
        string category = relative == "schemas.rbf" ? "Schema" : relative.StartsWith("state/") ? "State" :
            relative.StartsWith("journal/events/") ? "JournalEvent" : relative == "journal/refs/ref-op-log.rbf" ? "JournalRefOp" :
            relative.StartsWith("journal/refs/objects/") ? "JournalRefObject" : throw new InvalidOperationException($"Unexpected RBF file: {relative}");
        byte[] bytes = File.ReadAllBytes(path);
        using var file = RbfFile.OpenReadOnlyExisting(path);
        var scanner = file.ScanForward(showTombstone: true).GetEnumerator();
        while (scanner.MoveNext()) {
            var frame = scanner.Current;
            int offset = checked((int)frame.Ticket.Offset), length = checked((int)frame.Ticket.Length);
            inventory.Add(new { Path = relative, Category = category, Offset = offset, Length = length,
                frame.Tag, SHA256 = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset, length))) });
            categories.Add(category);
        }
        Require(scanner.TerminationError is null, $"Incomplete frame inventory: {relative}: {scanner.TerminationError}");
    }
    Require(categories.SetEquals(new[] { "Schema", "State", "JournalEvent", "JournalRefOp", "JournalRefObject" }), "Frame inventory missed a required store family.");
    WriteJson("old-frames.json", inventory);
}

void WriteJson<T>(string name, T value) => File.WriteAllText(Path.Combine(evidence, name),
    JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

static World Make(long value) {
    var world = new World { Value = value, Position = new Point { X = 20, Y = 30 } };
    world.Self = world.Alias = world;
    world.Links.Add(world);
    world.Links.Add(world);
    world.Named.Add("self", world);
    return world;
}

static void Validate(World world, long value) {
    Require(world.Value == value && ReferenceEquals(world, world.Self) && ReferenceEquals(world, world.Alias), "Value/cycle/alias changed.");
    Require(world.Position.X == 20 && world.Position.Y == 30 && world.Numbers.SequenceEqual(new long[] { 1, 2, 3 }), "Inline or array changed.");
    Require(world.Links.Count == 2 && world.Links.All(item => ReferenceEquals(world, item)) && world.Named.Count == 1 && ReferenceEquals(world, world.Named["self"]), "Container aliases changed.");
    Require(world.A == 11 && world.B == 22 && world.C == 33 && world.D == 44, "Unchanged scalar fields changed.");
}

static void Require(bool condition, string message) {
    if (!condition) { throw new InvalidOperationException(message); }
}

internal sealed record BranchWitness(string Name, string RefId, string[] Events, string[] Graphs);
