using System.Text;
using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed class SnapshotHistoryToolTests {
    [Fact]
    public void EmptyPublicationDoesNotCreateAHistoryDirectory() {
        using TempDirectory temp = new();
        string manifest = temp.Write("empty.g.cs", Manifest());
        string history = temp.Child("history");

        SnapshotHistoryResult result = new SnapshotHistoryTool().Publish(manifest, history);

        Assert.Equal("published 0 snapshot(s); 0 already exact", result.Message);
        Assert.False(Directory.Exists(history));
    }

    [Fact]
    public void PublishIsCreateOnlyAndIdempotentThenVerifySucceeds() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(
                new SnapshotFixture("samples.first", 1, (1, 2)),
                new SnapshotFixture("samples.second", 1, (4, 4))));
        string history = temp.Child("history");
        SnapshotHistoryTool tool = new();

        SnapshotHistoryResult first = tool.Publish(manifest, history);
        FileSnapshot[] before = ReadHistory(history);
        SnapshotHistoryResult second = tool.Publish(manifest, history);
        SnapshotHistoryResult verification = tool.Verify(manifest, history);
        FileSnapshot[] after = ReadHistory(history);

        Assert.Equal("published 2 snapshot(s); 0 already exact", first.Message);
        Assert.Equal("published 0 snapshot(s); 2 already exact", second.Message);
        Assert.Equal("verified 2 current snapshot(s) against 2 history snapshot(s)", verification.Message);
        Assert.Equal(before, after);
    }

    [Fact]
    public void VerifyFailsWhenCurrentSnapshotIsMissing() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SnapshotFixture("samples.example", 1, (1, 2))));

        SnapshotHistoryException exception = Assert.Throws<SnapshotHistoryException>(
            () => new SnapshotHistoryTool().Verify(manifest, temp.Child("missing")));

        Assert.Contains("history is missing schema 'samples.example' version 1", exception.Message);
        Assert.False(Directory.Exists(temp.Child("missing")));
    }

    [Fact]
    public void MalformedExistingHistoryFailsBeforePublication() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SnapshotFixture("samples.example", 1, (1, 2))));
        string history = temp.CreateDirectory("history");
        File.WriteAllText(
            Path.Combine(history, "broken.dgsnapshot"),
            "not a snapshot\n",
            new UTF8Encoding(false));

        Assert.Throws<SnapshotHistoryException>(
            () => new SnapshotHistoryTool().Publish(manifest, history));
        Assert.Single(Directory.GetFiles(history));
    }

    [Fact]
    public void BatchConflictDoesNotPublishEarlierPendingSnapshot() {
        using TempDirectory temp = new();
        string history = temp.Child("history");
        SnapshotHistoryTool tool = new();
        string accepted = temp.Write(
            "accepted.g.cs",
            Manifest(new SnapshotFixture("samples.example", 2, (1, 2))));
        tool.Publish(accepted, history);
        string conflictingBatch = temp.Write(
            "conflicting.g.cs",
            Manifest(
                new SnapshotFixture("samples.example", 1, (1, 2)),
                new SnapshotFixture("samples.example", 2, (1, 3))));

        Assert.Throws<SnapshotHistoryException>(
            () => tool.Publish(conflictingBatch, history));

        string remaining = Assert.Single(Directory.GetFiles(history));
        Assert.Contains("// version:2\n", File.ReadAllText(remaining), StringComparison.Ordinal);
    }

    [Fact]
    public void NonCanonicalHistoryBytesAreRejected() {
        using TempDirectory temp = new();
        string history = temp.Child("history");
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SnapshotFixture("samples.example", 1, (1, 2))));
        SnapshotHistoryTool tool = new();
        tool.Publish(manifest, history);
        string historyFile = Assert.Single(Directory.GetFiles(history));
        string canonical = File.ReadAllText(historyFile);
        File.WriteAllText(
            historyFile,
            canonical.Replace("\n", "\r\n", StringComparison.Ordinal),
            new UTF8Encoding(false));

        SnapshotHistoryException exception = Assert.Throws<SnapshotHistoryException>(
            () => tool.Verify(manifest, history));

        Assert.Contains("content is not canonical UTF-8 with LF line endings", exception.Message);
    }

    [Fact]
    public void DuplicateManifestKeyIsRejected() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "duplicates.g.cs",
            Manifest(
                new SnapshotFixture("samples.example", 1, (1, 2)),
                new SnapshotFixture("samples.example", 1, (1, 2))));

        Assert.Throws<SnapshotHistoryException>(
            () => new SnapshotHistoryTool().Publish(manifest, temp.Child("history")));
    }

    private static string Manifest(params SnapshotFixture[] snapshots) {
        StringBuilder builder = new();
        builder.AppendLine("// durable-graph-snapshot-manifest:1");

        foreach (SnapshotFixture snapshot in snapshots) {
            builder.AppendLine("// snapshot-begin");
            builder.Append("// schema-id-base64:")
                .AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot.SchemaId)));
            builder.Append("// version:").AppendLine(snapshot.Version.ToString());

            foreach ((int fieldId, int typeTag) in snapshot.Fields) {
                builder.Append("// field:")
                    .Append(fieldId)
                    .Append('|')
                    .AppendLine(typeTag.ToString());
            }

            builder.AppendLine("// snapshot-end");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static FileSnapshot[] ReadHistory(string history) {
        return Directory.GetFiles(history)
            .Order(StringComparer.Ordinal)
            .Select(path => new FileSnapshot(
                Path.GetFileName(path),
                File.GetLastWriteTimeUtc(path),
                Convert.ToHexString(File.ReadAllBytes(path))))
            .ToArray();
    }

    private sealed record SnapshotFixture(
        string SchemaId,
        int Version,
        params (int FieldId, int TypeTag)[] Fields);

    private sealed record FileSnapshot(
        string Name,
        DateTime LastWriteTimeUtc,
        string ContentHex);

    private sealed class TempDirectory : IDisposable {
        public TempDirectory() {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Atelia.DurableGraph.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Child(string name) => System.IO.Path.Combine(Path, name);

        public string CreateDirectory(string name) {
            string path = Child(name);
            Directory.CreateDirectory(path);
            return path;
        }

        public string Write(string name, string contents) {
            string path = Child(name);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
            return path;
        }

        public void Dispose() {
            Directory.Delete(Path, recursive: true);
        }
    }
}
