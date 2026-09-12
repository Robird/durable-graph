using Atelia.Data;
using Atelia.EventJournal;
using JournalStore = Atelia.EventJournal.EventJournal;
using StateFrameAddress = Atelia.DurableGraph.Storage.FrameAddress;

namespace Atelia.DurableGraph.Persistence.Tests;

public sealed class HistoryJournalTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"durable-graph-history-journal-{Guid.NewGuid():N}");
    private static readonly StateFrameAddress Revision = new(1, SizedPtr.Create(4, 24));

    [Fact]
    public void ReopenRetainsPhysicalOrphansAndReadOnlyDoesNotWrite() {
        EventAddress first;
        EventAddress orphan;
        using (HistoryJournal writer = HistoryJournal.Create(_root)) {
            Assert.Throws<InvalidOperationException>(() => writer.Append(GraphFrameKind.State, Revision, new ObjectId(1), null));
            writer.ConfirmDurable();
            first = writer.Append(GraphFrameKind.State, Revision, new ObjectId(1), null);
            writer.Journal.CreateBranch("main", first).Unwrap();
            orphan = writer.Append(GraphFrameKind.Event, Revision, new ObjectId(2), first);
            Assert.Equal(2, writer.ReadAllFrames().Count);
        }
        Assert.False(File.Exists(Path.Combine(_root, "publication.rbf")));
        var before = Snapshot();
        using (HistoryJournal reader = HistoryJournal.Open(_root, readOnly: true)) {
            Assert.Equal(new[] { first, orphan }, reader.ReadAllFrames().Select(frame => frame.Address));
            Assert.Equal(first, reader.Journal.GetHead(reader.Journal.OpenBranch("main").Unwrap()));
            Assert.Equal(first, reader.Read(orphan).Parent);
            Assert.Equal(new ObjectId(2), reader.Read(orphan).RootId);
            Assert.Throws<InvalidOperationException>(reader.ConfirmDurable);
            Assert.Throws<InvalidOperationException>(() => reader.Append(GraphFrameKind.Event, Revision, new ObjectId(3), first));
            Assert.Throws<InvalidOperationException>(() => reader.Journal.CreateBranch("forbidden", first));
            using HistoryJournal secondReader = HistoryJournal.Open(_root, readOnly: true);
            Assert.Equal(2, secondReader.ReadAllFrames().Count);
        }
        AssertUnchanged(before);
        using HistoryJournal reopened = HistoryJournal.Open(_root, readOnly: false);
        Assert.Throws<InvalidOperationException>(() => reopened.Append(GraphFrameKind.Event, Revision, new ObjectId(3), first));
        reopened.ConfirmDurable();
        EventAddress next = reopened.Append(GraphFrameKind.Event, Revision, new ObjectId(3), first);
        Assert.Equal(next, reopened.ReadAllFrames()[2].Address);
    }

    [Fact]
    public void RepositoryLockCoversReadersWritersAndInitialValidation() {
        using (HistoryJournal writer = HistoryJournal.Create(_root)) {
            writer.ConfirmDurable();
            Assert.Throws<IOException>(() => HistoryJournal.Open(_root, readOnly: false));
            Assert.Throws<IOException>(() => HistoryJournal.Open(_root, readOnly: true));
        }
        using HistoryJournal reader = HistoryJournal.Open(_root, readOnly: true);
        Assert.Throws<IOException>(() => HistoryJournal.Open(_root, readOnly: false));
    }

    [Fact]
    public void ConfirmationOrdersEventDependenciesBeforeRefObjectsAndNamePublicationLast() {
        CreatePopulated();
        string journalPath = Path.Combine(_root, "journal");
        string unselected = Path.Combine(journalPath, "unselected.rbf");
        File.Copy(ComponentFile("events"), unselected);
        string eventFile = ComponentFile("events");
        string refObjectFile = ComponentFile("ref-object");
        string refOpFile = ComponentFile("ref-op");
        List<string> confirmed = [];
        using HistoryJournal journal = HistoryJournal.Open(_root, readOnly: false);
        journal.AfterConfirmFile = confirmed.Add;
        journal.ConfirmDurable();
        Assert.Equal(new[] { eventFile, refObjectFile, unselected, refOpFile }, confirmed);
        journal.ConfirmDurable();
        Assert.Equal(4, confirmed.Count);
    }

    [Fact]
    public void FailureAfterEventBarrierNeverConfirmsRefsAndDisposalReleasesLock() {
        CreatePopulated();
        List<string> confirmed = [];
        using (HistoryJournal journal = HistoryJournal.Open(_root, readOnly: false)) {
            journal.AfterConfirmFile = path => {
                confirmed.Add(path);
                throw new IOException("Interrupted after event dependency confirmation.");
            };
            Assert.Throws<IOException>(journal.ConfirmDurable);
            Assert.Equal(new[] { ComponentFile("events") }, confirmed);
            Assert.Throws<InvalidOperationException>(() => journal.Append(GraphFrameKind.State, Revision, new ObjectId(1), null));
        }
        using HistoryJournal reopened = HistoryJournal.Open(_root, readOnly: false);
        reopened.ConfirmDurable();
        Assert.Equal(2, reopened.ReadAllFrames().Count);
    }

    [Fact]
    public void ConfirmationValidatesAllFilesBeforeFlushingAnyFile() {
        CreatePopulated();
        using HistoryJournal journal = HistoryJournal.Open(_root, readOnly: false);
        string damaged = Path.Combine(_root, "journal", "unselected.rbf");
        // A later-discovered file is covered even when the initial readonly open already succeeded.
        File.WriteAllBytes(damaged, new byte[] { 1, 2, 3 });
        List<string> confirmed = [];
        journal.AfterConfirmFile = confirmed.Add;
        Assert.Throws<InvalidDataException>(journal.ConfirmDurable);
        Assert.Empty(confirmed);
    }

    [Theory]
    [InlineData("events", false, false)]
    [InlineData("events", false, true)]
    [InlineData("events", true, false)]
    [InlineData("events", true, true)]
    [InlineData("ref-op", false, false)]
    [InlineData("ref-op", false, true)]
    [InlineData("ref-op", true, false)]
    [InlineData("ref-op", true, true)]
    [InlineData("ref-object", false, false)]
    [InlineData("ref-object", false, true)]
    [InlineData("ref-object", true, false)]
    [InlineData("ref-object", true, true)]
    public void EveryJournalComponentRejectsDamagedTailWithoutRepair(string component, bool truncate, bool readOnly) {
        CreatePopulated();
        string path = ComponentFile(component);
        byte[] bytes = File.ReadAllBytes(path);
        if (truncate) { bytes = bytes[..^1]; }
        else { bytes[^8] ^= 0x20; }
        File.WriteAllBytes(path, bytes);
        var before = Snapshot();
        Assert.ThrowsAny<Exception>(() => {
            using HistoryJournal rejected = HistoryJournal.Open(_root, readOnly);
        });
        AssertUnchanged(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownKindInPhysicalOrphanIsRejected(bool readOnly) {
        CreatePopulated();
        using (JournalStore raw = JournalStore.OpenExisting(Path.Combine(_root, "journal"))) {
            raw.AppendEventFrame(null, GraphEnvelopeCodec.Encode(Revision, new ObjectId(8)), 99).Unwrap();
        }
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => HistoryJournal.Open(_root, readOnly));
        AssertUnchanged(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnselectedRefFileBadTailIsNotHiddenByLiveBranchReplay(bool readOnly) {
        CreatePopulated();
        string unselected = Path.Combine(_root, "journal", "refs", "objects", "unselected.rbf");
        byte[] bytes = File.ReadAllBytes(ComponentFile("ref-object"));
        File.WriteAllBytes(unselected, bytes[..^4]);
        var before = Snapshot();
        Assert.ThrowsAny<Exception>(() => {
            using HistoryJournal rejected = HistoryJournal.Open(_root, readOnly);
        });
        AssertUnchanged(before);
    }

    [Fact]
    public void MissingReadOnlyResourcesAreNeverCreated() {
        Assert.Throws<DirectoryNotFoundException>(() => HistoryJournal.Open(_root, readOnly: true));
        Assert.False(Directory.Exists(_root));
        Directory.CreateDirectory(_root);
        Assert.Throws<FileNotFoundException>(() => HistoryJournal.Open(_root, readOnly: true));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
        File.WriteAllBytes(Path.Combine(_root, "repository.lock"), []);
        var before = Snapshot();
        Assert.Throws<DirectoryNotFoundException>(() => HistoryJournal.Open(_root, readOnly: true));
        AssertUnchanged(before);
    }

    private void CreatePopulated() {
        using HistoryJournal writer = HistoryJournal.Create(_root);
        writer.ConfirmDurable();
        EventAddress initial = writer.Append(GraphFrameKind.State, Revision, new ObjectId(1), null);
        writer.Journal.CreateBranch("main", initial).Unwrap();
        // The last physical event is an orphan and still belongs to strict validation.
        writer.Append(GraphFrameKind.Event, Revision, new ObjectId(2), initial);
    }

    private string ComponentFile(string component) => component switch {
        "events" => Assert.Single(Directory.GetFiles(Path.Combine(_root, "journal", "events"), "*.rbf", SearchOption.AllDirectories)),
        "ref-op" => Path.Combine(_root, "journal", "refs", "ref-op-log.rbf"),
        "ref-object" => Assert.Single(Directory.GetFiles(Path.Combine(_root, "journal", "refs", "objects"), "*.rbf", SearchOption.AllDirectories)),
        _ => throw new InvalidOperationException(component),
    };

    private Dictionary<string, (byte[] Bytes, DateTime LastWrite)> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));

    private void AssertUnchanged(Dictionary<string, (byte[] Bytes, DateTime LastWrite)> before) {
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, saved) in before) {
            Assert.Equal(saved.Bytes, File.ReadAllBytes(path));
            Assert.Equal(saved.LastWrite, File.GetLastWriteTimeUtc(path));
        }
    }

    public void Dispose() {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
