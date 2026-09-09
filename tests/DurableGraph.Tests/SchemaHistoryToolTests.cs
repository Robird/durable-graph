using System.Text;
using Atelia.DurableGraph.Build;

namespace Atelia.DurableGraph.Tests;

public sealed class SchemaHistoryToolTests {
    [Fact]
    public void EmptyPublicationDoesNotCreateAHistoryDirectory() {
        using TempDirectory temp = new();
        string manifest = temp.Write("empty.g.cs", Manifest());
        string history = temp.Child("history");

        SchemaHistoryResult result = new SchemaHistoryTool().Publish(manifest, history);

        Assert.Equal("published 0 schema-history record(s); 0 already exact", result.Message);
        Assert.False(Directory.Exists(history));
    }

    [Fact]
    public void PublishIsCreateOnlyAndIdempotentThenVerifySucceeds() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(
                new SchemaHistoryFixture("samples.first", 1, (1, 2)),
                new SchemaHistoryFixture("samples.second", 1, (4, 4))));
        string history = temp.Child("history");
        SchemaHistoryTool tool = new();

        SchemaHistoryResult first = tool.Publish(manifest, history);
        HistoryFileState[] before = ReadHistory(history);
        SchemaHistoryResult second = tool.Publish(manifest, history);
        SchemaHistoryResult verification = tool.Verify(manifest, history);
        HistoryFileState[] after = ReadHistory(history);

        Assert.Equal("published 2 schema-history record(s); 0 already exact", first.Message);
        Assert.Equal("published 0 schema-history record(s); 2 already exact", second.Message);
        Assert.Equal("verified 2 current manifest candidate(s) against 2 schema-history record(s)", verification.Message);
        Assert.Equal(before, after);
    }

    [Fact]
    public void VerifyFailsWhenCurrentSchemaIsMissing() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SchemaHistoryFixture("samples.example", 1, (1, 2))));

        SchemaHistoryException exception = Assert.Throws<SchemaHistoryException>(
            () => new SchemaHistoryTool().Verify(manifest, temp.Child("missing")));

        Assert.Contains("history is missing schema 'samples.example' version 1", exception.Message);
        Assert.False(Directory.Exists(temp.Child("missing")));
    }

    [Fact]
    public void MalformedExistingHistoryFailsBeforePublication() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SchemaHistoryFixture("samples.example", 1, (1, 2))));
        string history = temp.CreateDirectory("history");
        File.WriteAllText(
            Path.Combine(history, "broken.dgschema"),
            "not Schema history\n",
            new UTF8Encoding(false));

        Assert.Throws<SchemaHistoryException>(
            () => new SchemaHistoryTool().Publish(manifest, history));
        Assert.Single(Directory.GetFiles(history));
    }

    [Fact]
    public void BatchConflictDoesNotPublishEarlierPendingSchemaRecord() {
        using TempDirectory temp = new();
        string history = temp.Child("history");
        SchemaHistoryTool tool = new();
        string accepted = temp.Write(
            "accepted.g.cs",
            Manifest(new SchemaHistoryFixture("samples.example", 2, (1, 2))));
        tool.Publish(accepted, history);
        string conflictingBatch = temp.Write(
            "conflicting.g.cs",
            Manifest(
                new SchemaHistoryFixture("samples.example", 1, (1, 2)),
                new SchemaHistoryFixture("samples.example", 2, (1, 3))));

        Assert.Throws<SchemaHistoryException>(
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
            Manifest(new SchemaHistoryFixture("samples.example", 1, (1, 2))));
        SchemaHistoryTool tool = new();
        tool.Publish(manifest, history);
        string historyFile = Assert.Single(Directory.GetFiles(history));
        string canonical = File.ReadAllText(historyFile);
        File.WriteAllText(
            historyFile,
            canonical.Replace("\n", "\r\n", StringComparison.Ordinal),
            new UTF8Encoding(false));

        SchemaHistoryException exception = Assert.Throws<SchemaHistoryException>(
            () => tool.Verify(manifest, history));

        Assert.Contains("content is not canonical UTF-8 with LF line endings", exception.Message);
    }

    [Fact]
    public void DuplicateManifestKeyIsRejected() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "duplicates.g.cs",
            Manifest(
                new SchemaHistoryFixture("samples.example", 1, (1, 2)),
                new SchemaHistoryFixture("samples.example", 1, (1, 2))));

        Assert.Throws<SchemaHistoryException>(
            () => new SchemaHistoryTool().Publish(manifest, temp.Child("history")));
    }

    [Fact]
    public void PublicationUsesCanonicalSchemaHistoryContentAndFileName() {
        using TempDirectory temp = new();
        string manifest = temp.Write(
            "candidates.g.cs",
            Manifest(new SchemaHistoryFixture("Base", 1, (1, 2))));
        string history = temp.Child("history");

        new SchemaHistoryTool().Publish(manifest, history);

        string path = Assert.Single(Directory.GetFiles(history));
        Assert.Equal(
            "schema.7b47361aad19bb483aeab081a7df0a55f7cb0fdb3327efe6dd016e6353a17880.V1.36d15f4f25b1ababd37803603d32c3f73a5364019b684b7c4c5a0fab09204ff1.dgschema",
            Path.GetFileName(path));
        Assert.Equal(
            "// durable-graph-schema-history:6\n" +
            "// schema-begin\n" +
            "// schema-id-base64:QmFzZQ==\n" +
            "// version:1\n" +
            "// kind:1\n" +
            "// arity:0\n" +
            "// field:1|2\n" +
            "// schema-end\n",
            File.ReadAllText(path));
    }

    [Theory]
    [InlineData(
        "// durable-graph-snapshot:1\n// schema-begin\n// schema-id-base64:QmFzZQ==\n// version:1\n// field:1|2\n// schema-end\n")]
    [InlineData(
        "// durable-graph-schema-history:1\n// snapshot-begin\n// schema-id-base64:QmFzZQ==\n// version:1\n// field:1|2\n// snapshot-end\n")]
    public void DgschemaRejectsLegacyHeaderOrRecordMarkers(string legacyContent) {
        using TempDirectory temp = new();
        string manifest = temp.Write("candidates.g.cs", Manifest());
        string history = temp.CreateDirectory("history");
        File.WriteAllText(
            Path.Combine(history, "legacy.dgschema"),
            legacyContent,
            new UTF8Encoding(false));

        Assert.Throws<SchemaHistoryException>(
            () => new SchemaHistoryTool().Publish(manifest, history));
    }

    [Fact]
    public void ConfiguredHistoryRejectsLegacyDgsnapshotInsteadOfIgnoringIt() {
        using TempDirectory temp = new();
        string manifest = temp.Write("candidates.g.cs", Manifest());
        string history = temp.CreateDirectory("history");
        File.WriteAllText(
            Path.Combine(history, "legacy.dgsnapshot"),
            "// durable-graph-snapshot:1\n" +
            "// snapshot-begin\n" +
            "// schema-id-base64:QmFzZQ==\n" +
            "// version:1\n" +
            "// field:1|2\n" +
            "// snapshot-end\n",
            new UTF8Encoding(false));

        SchemaHistoryException exception = Assert.Throws<SchemaHistoryException>(
            () => new SchemaHistoryTool().Publish(manifest, history));

        Assert.Contains(".dgsnapshot", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(history, "*.dgschema"));
    }

    private static string Manifest(params SchemaHistoryFixture[] records) {
        StringBuilder builder = new();
        builder.AppendLine("// durable-graph-schema-history-manifest:1");

        foreach (SchemaHistoryFixture record in records) {
            builder.AppendLine("// schema-begin");
            builder.Append("// schema-id-base64:")
                .AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(record.SchemaId)));
            builder.Append("// version:").AppendLine(record.Version.ToString());

            foreach ((int fieldId, int typeTag) in record.Fields) {
                builder.Append("// field:")
                    .Append(fieldId)
                    .Append('|')
                    .AppendLine(typeTag.ToString());
            }

            builder.AppendLine("// schema-end");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static HistoryFileState[] ReadHistory(string history) {
        return Directory.GetFiles(history)
            .Order(StringComparer.Ordinal)
            .Select(path => new HistoryFileState(
                Path.GetFileName(path),
                File.GetLastWriteTimeUtc(path),
                Convert.ToHexString(File.ReadAllBytes(path))))
            .ToArray();
    }

    private sealed record SchemaHistoryFixture(
        string SchemaId,
        int Version,
        params (int FieldId, int TypeTag)[] Fields);

    private sealed record HistoryFileState(
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
