using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atelia.DurableGraph.Build;

internal sealed class SnapshotHistoryTool {
    private const string SnapshotExtension = ".dgsnapshot";

    public SnapshotHistoryResult Publish(
        string manifestPath,
        string historyDirectory) {
        IReadOnlyList<SnapshotRecord> candidates = SnapshotDocument.ParseManifest(manifestPath);

        if (candidates.Count == 0 && !Directory.Exists(historyDirectory)) {
            return new SnapshotHistoryResult(
                "published 0 snapshot(s); 0 already exact");
        }

        Dictionary<SnapshotKey, ExistingSnapshot> existing = Directory.Exists(historyDirectory)
            ? LoadHistory(historyDirectory)
            : new Dictionary<SnapshotKey, ExistingSnapshot>();
        Dictionary<SnapshotKey, SnapshotRecord> available = existing.ToDictionary(
            pair => pair.Key, pair => pair.Value.Snapshot);

        foreach (SnapshotRecord candidate in candidates) {
            if (available.TryGetValue(candidate.Key, out SnapshotRecord? historical) &&
                !candidate.ShapeEquals(historical)) {
                throw new SnapshotHistoryException(
                    $"history conflicts with manifest for schema '{candidate.SchemaId}' version {candidate.Version}");
            }

            available[candidate.Key] = candidate;
        }

        ValidateClosure(available);
        List<PendingSnapshot> pending = new();
        int unchangedCount = 0;

        foreach (SnapshotRecord candidate in candidates) {
            SnapshotKey key = candidate.Key;

            if (existing.ContainsKey(key)) {
                unchangedCount++;
                continue;
            }

            string canonicalContent = SnapshotDocument.RenderHistory(candidate);
            string fileName = SnapshotDocument.GetHistoryFileName(candidate, canonicalContent);
            string destinationPath = Path.Combine(historyDirectory, fileName);

            if (File.Exists(destinationPath)) {
                throw new SnapshotHistoryException(
                    $"history destination '{fileName}' already exists but was not a valid indexed snapshot");
            }

            pending.Add(new PendingSnapshot(destinationPath, canonicalContent));
        }

        Directory.CreateDirectory(historyDirectory);

        foreach (PendingSnapshot snapshot in pending) {
            PublishCreateOnly(snapshot);
        }

        return new SnapshotHistoryResult(
            $"published {pending.Count} snapshot(s); {unchangedCount} already exact");
    }

    public SnapshotHistoryResult Verify(
        string manifestPath,
        string historyDirectory) {
        IReadOnlyList<SnapshotRecord> candidates = SnapshotDocument.ParseManifest(manifestPath);
        Dictionary<SnapshotKey, ExistingSnapshot> existing = Directory.Exists(historyDirectory)
            ? LoadHistory(historyDirectory)
            : new Dictionary<SnapshotKey, ExistingSnapshot>();

        foreach (SnapshotRecord candidate in candidates) {
            if (!existing.TryGetValue(candidate.Key, out ExistingSnapshot? historical)) {
                throw new SnapshotHistoryException(
                    $"history is missing schema '{candidate.SchemaId}' version {candidate.Version}");
            }

            if (!candidate.ShapeEquals(historical.Snapshot)) {
                throw new SnapshotHistoryException(
                    $"history conflicts with manifest for schema '{candidate.SchemaId}' version {candidate.Version}");
            }
        }

        return new SnapshotHistoryResult(
            $"verified {candidates.Count} current snapshot(s) against {existing.Count} history snapshot(s)");
    }

    private static Dictionary<SnapshotKey, ExistingSnapshot> LoadHistory(
        string historyDirectory) {
        Dictionary<SnapshotKey, ExistingSnapshot> snapshots = new();
        string[] paths = Directory.GetFiles(
            historyDirectory,
            $"*{SnapshotExtension}",
            SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);

        foreach (string path in paths) {
            SnapshotRecord snapshot = SnapshotDocument.ParseHistory(path);
            string canonicalContent = SnapshotDocument.RenderHistory(snapshot);
            string expectedFileName = SnapshotDocument.GetHistoryFileName(snapshot, canonicalContent);
            string actualFileName = Path.GetFileName(path);

            if (!StringComparer.Ordinal.Equals(actualFileName, expectedFileName)) {
                throw new SnapshotHistoryException(
                    $"history file '{actualFileName}' must be named '{expectedFileName}'");
            }

            if (snapshots.TryGetValue(snapshot.Key, out ExistingSnapshot? duplicate)) {
                string conflict = snapshot.ShapeEquals(duplicate.Snapshot)
                    ? "duplicates"
                    : "conflicts with";
                throw new SnapshotHistoryException(
                    $"history file '{actualFileName}' {conflict} '{duplicate.FileName}' for schema '{snapshot.SchemaId}' version {snapshot.Version}");
            }

            snapshots.Add(
                snapshot.Key,
                new ExistingSnapshot(actualFileName, snapshot));
        }

        // Accepted history must close by itself. A current candidate cannot repair it.
        ValidateClosure(snapshots.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot));
        return snapshots;
    }

    private static void ValidateClosure(IReadOnlyDictionary<SnapshotKey, SnapshotRecord> snapshots) {
        foreach (SnapshotRecord snapshot in snapshots.Values) {
            HashSet<string> ancestors = new(StringComparer.Ordinal) { snapshot.SchemaId };
            SnapshotRecord current = snapshot;

            while (current.BaseSchema is SnapshotKey baseKey) {
                if (!ancestors.Add(baseKey.SchemaId)) {
                    throw new SnapshotHistoryException(
                        $"schema '{snapshot.SchemaId}' version {snapshot.Version} repeats ancestor schema '{baseKey.SchemaId}'");
                }

                if (!snapshots.TryGetValue(baseKey, out SnapshotRecord? baseSchema)) {
                    throw new SnapshotHistoryException(
                        $"schema '{current.SchemaId}' version {current.Version} is missing base schema '{baseKey.SchemaId}' version {baseKey.Version}");
                }

                current = baseSchema;
            }
        }
    }

    private static void PublishCreateOnly(PendingSnapshot snapshot) {
        string directory = Path.GetDirectoryName(snapshot.DestinationPath)
            ?? throw new SnapshotHistoryException("history destination has no directory");
        string fileName = Path.GetFileName(snapshot.DestinationPath);
        string temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");

        try {
            byte[] bytes = SnapshotDocument.Utf8NoBom.GetBytes(snapshot.Content);

            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, snapshot.DestinationPath, overwrite: false);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record ExistingSnapshot(string FileName, SnapshotRecord Snapshot);

    private sealed record PendingSnapshot(string DestinationPath, string Content);
}

internal static class SnapshotDocument {
    private const string ManifestHeader = "// durable-graph-snapshot-manifest:1";
    private const string HistoryHeader = "// durable-graph-snapshot:1";
    private const string SnapshotBegin = "// snapshot-begin";
    private const string SnapshotEnd = "// snapshot-end";
    private const string SchemaIdPrefix = "// schema-id-base64:";
    private const string VersionPrefix = "// version:";
    private const string BasePrefix = "// base:";
    private const string FieldPrefix = "// field:";

    internal static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<SnapshotRecord> ParseManifest(string path) {
        string text = ReadUtf8(path, allowByteOrderMark: true);
        IReadOnlyList<SnapshotRecord> snapshots = Parse(
            path,
            text,
            ManifestHeader,
            requireExactlyOneSnapshot: false);
        Dictionary<SnapshotKey, SnapshotRecord> distinct = new();

        foreach (SnapshotRecord snapshot in snapshots) {
            if (distinct.TryGetValue(snapshot.Key, out SnapshotRecord? duplicate)) {
                string reason = snapshot.ShapeEquals(duplicate)
                    ? "duplicates a snapshot"
                    : "has a conflicting shape";
                throw Invalid(
                    path,
                    $"schema '{snapshot.SchemaId}' version {snapshot.Version} {reason}");
            }

            distinct.Add(snapshot.Key, snapshot);
        }

        return snapshots;
    }

    public static SnapshotRecord ParseHistory(string path) {
        byte[] bytes;

        try {
            bytes = File.ReadAllBytes(path);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            throw Invalid(path, exception.Message);
        }

        string text = DecodeUtf8(path, bytes, allowByteOrderMark: false);
        SnapshotRecord snapshot = Parse(
            path,
            text,
            HistoryHeader,
            requireExactlyOneSnapshot: true)[0];
        byte[] canonicalBytes = Utf8NoBom.GetBytes(RenderHistory(snapshot));

        if (!bytes.AsSpan().SequenceEqual(canonicalBytes)) {
            throw Invalid(path, "content is not canonical UTF-8 with LF line endings");
        }

        return snapshot;
    }

    public static string RenderHistory(SnapshotRecord snapshot) {
        StringBuilder builder = new();
        builder.AppendLine(HistoryHeader);
        AppendSnapshot(builder, snapshot);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string GetHistoryFileName(
        SnapshotRecord snapshot,
        string canonicalContent) {
        string schemaHash = ToLowerHex(SHA256.HashData(Utf8NoBom.GetBytes(snapshot.SchemaId)));
        string contentHash = ToLowerHex(SHA256.HashData(Utf8NoBom.GetBytes(canonicalContent)));
        return $"snapshot.{schemaHash}.V{snapshot.Version.ToString(CultureInfo.InvariantCulture)}.{contentHash}.dgsnapshot";
    }

    private static IReadOnlyList<SnapshotRecord> Parse(
        string path,
        string text,
        string expectedHeader,
        bool requireExactlyOneSnapshot) {
        string[] lines = SplitLines(path, text);

        if (lines.Length == 0 || !StringComparer.Ordinal.Equals(lines[0], expectedHeader)) {
            throw Invalid(path, $"expected header '{expectedHeader}'");
        }

        List<SnapshotRecord> snapshots = new();
        int index = 1;

        while (index < lines.Length) {
            if (!StringComparer.Ordinal.Equals(lines[index], SnapshotBegin)) {
                throw Invalid(path, $"line {index + 1} must be '{SnapshotBegin}'");
            }

            index++;
            string schemaIdBase64 = ReadPrefixedLine(
                path,
                lines,
                ref index,
                SchemaIdPrefix);
            string schemaId = DecodeSchemaId(path, schemaIdBase64);
            string versionText = ReadPrefixedLine(
                path,
                lines,
                ref index,
                VersionPrefix);
            int version = ParsePositiveCanonicalInt(path, versionText, "version");
            SnapshotKey? baseSchema = null;

            if (index < lines.Length && lines[index].StartsWith(BasePrefix, StringComparison.Ordinal)) {
                string baseText = lines[index].Substring(BasePrefix.Length);
                int separator = baseText.IndexOf('|');

                if (separator <= 0 || separator != baseText.LastIndexOf('|')) {
                    throw Invalid(path, $"line {index + 1} has an invalid base entry");
                }

                baseSchema = new SnapshotKey(
                    DecodeSchemaId(path, baseText.Substring(0, separator)),
                    ParsePositiveCanonicalInt(path, baseText.Substring(separator + 1), "base version"));
                index++;
            }

            List<SnapshotField> fields = new();
            int previousFieldId = 0;

            while (index < lines.Length &&
                lines[index].StartsWith(FieldPrefix, StringComparison.Ordinal)) {
                string fieldText = lines[index].Substring(FieldPrefix.Length);
                int separator = fieldText.IndexOf('|');

                if (separator <= 0 || separator != fieldText.LastIndexOf('|')) {
                    throw Invalid(path, $"line {index + 1} has an invalid field entry");
                }

                int fieldId = ParsePositiveCanonicalInt(
                    path,
                    fieldText.Substring(0, separator),
                    $"field ID on line {index + 1}");
                int typeTag = ParsePositiveCanonicalInt(
                    path,
                    fieldText.Substring(separator + 1),
                    $"TypeTag on line {index + 1}");

                if (fieldId <= previousFieldId) {
                    throw Invalid(
                        path,
                        $"field IDs must be unique and sorted; line {index + 1} has {fieldId} after {previousFieldId}");
                }

                if (typeTag is < 1 or > 4) {
                    throw Invalid(path, $"line {index + 1} has unsupported TypeTag {typeTag}");
                }

                fields.Add(new SnapshotField(fieldId, typeTag));
                previousFieldId = fieldId;
                index++;
            }

            if (index >= lines.Length || !StringComparer.Ordinal.Equals(lines[index], SnapshotEnd)) {
                throw Invalid(path, $"snapshot for schema '{schemaId}' version {version} has no '{SnapshotEnd}'");
            }

            index++;
            snapshots.Add(new SnapshotRecord(schemaId, schemaIdBase64, version, fields, baseSchema));
        }

        if (requireExactlyOneSnapshot && snapshots.Count != 1) {
            throw Invalid(path, $"history must contain exactly one snapshot block, found {snapshots.Count}");
        }

        return snapshots;
    }

    private static string[] SplitLines(string path, string text) {
        if (text.IndexOf('\r') >= 0) {
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal);

            if (text.IndexOf('\r') >= 0) {
                throw Invalid(path, "contains an unsupported carriage return");
            }
        }

        string[] lines = text.Split('\n');

        if (lines.Length > 0 && lines[^1].Length == 0) {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static string ReadPrefixedLine(
        string path,
        string[] lines,
        ref int index,
        string prefix) {
        if (index >= lines.Length || !lines[index].StartsWith(prefix, StringComparison.Ordinal)) {
            throw Invalid(path, $"line {index + 1} must start with '{prefix}'");
        }

        string value = lines[index].Substring(prefix.Length);
        index++;
        return value;
    }

    private static string DecodeSchemaId(string path, string value) {
        byte[] bytes;

        try {
            bytes = Convert.FromBase64String(value);
        } catch (FormatException) {
            throw Invalid(path, "schema-id-base64 is not valid Base64");
        }

        if (!StringComparer.Ordinal.Equals(Convert.ToBase64String(bytes), value)) {
            throw Invalid(path, "schema-id-base64 is not canonical Base64");
        }

        string schemaId;

        try {
            schemaId = Utf8NoBom.GetString(bytes);
        } catch (DecoderFallbackException) {
            throw Invalid(path, "schema-id-base64 does not encode valid UTF-8");
        }

        if (string.IsNullOrWhiteSpace(schemaId)) {
            throw Invalid(path, "schema ID must not be empty or whitespace");
        }

        return schemaId;
    }

    private static int ParsePositiveCanonicalInt(
        string path,
        string value,
        string description) {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int result) ||
            result <= 0 ||
            !StringComparer.Ordinal.Equals(
                result.ToString(CultureInfo.InvariantCulture),
                value)) {
            throw Invalid(path, $"{description} must be a canonical positive Int32");
        }

        return result;
    }

    private static string ReadUtf8(string path, bool allowByteOrderMark) {
        byte[] bytes;

        try {
            bytes = File.ReadAllBytes(path);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            throw Invalid(path, exception.Message);
        }

        return DecodeUtf8(path, bytes, allowByteOrderMark);
    }

    private static string DecodeUtf8(
        string path,
        byte[] bytes,
        bool allowByteOrderMark) {
        ReadOnlySpan<byte> content = bytes;

        const int utf8ByteOrderMarkLength = 3;
        bool hasUtf8ByteOrderMark =
            content.Length >= utf8ByteOrderMarkLength &&
            content[0] == 0xef &&
            content[1] == 0xbb &&
            content[2] == 0xbf;

        if (hasUtf8ByteOrderMark) {
            if (!allowByteOrderMark) {
                throw Invalid(path, "UTF-8 byte-order marks are not canonical history content");
            }

            content = content.Slice(utf8ByteOrderMarkLength);
        }

        try {
            return Utf8NoBom.GetString(content);
        } catch (DecoderFallbackException) {
            throw Invalid(path, "is not valid UTF-8");
        }
    }

    private static void AppendSnapshot(
        StringBuilder builder,
        SnapshotRecord snapshot) {
        builder.AppendLine(SnapshotBegin);
        builder.Append(SchemaIdPrefix).AppendLine(snapshot.SchemaIdBase64);
        builder.Append(VersionPrefix)
            .AppendLine(snapshot.Version.ToString(CultureInfo.InvariantCulture));

        if (snapshot.BaseSchema is SnapshotKey baseSchema) {
            builder.Append(BasePrefix)
                .Append(Convert.ToBase64String(Utf8NoBom.GetBytes(baseSchema.SchemaId)))
                .Append('|')
                .AppendLine(baseSchema.Version.ToString(CultureInfo.InvariantCulture));
        }

        foreach (SnapshotField field in snapshot.Fields) {
            builder.Append(FieldPrefix)
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .AppendLine(field.TypeTag.ToString(CultureInfo.InvariantCulture));
        }

        builder.AppendLine(SnapshotEnd);
    }

    private static string ToLowerHex(byte[] bytes) {
        return Convert.ToHexStringLower(bytes);
    }

    private static SnapshotHistoryException Invalid(string path, string reason) {
        return new SnapshotHistoryException(
            $"snapshot file '{Path.GetFileName(path)}' is invalid: {reason}");
    }
}

internal sealed class SnapshotRecord {
    public SnapshotRecord(
        string schemaId,
        string schemaIdBase64,
        int version,
        IReadOnlyList<SnapshotField> fields,
        SnapshotKey? baseSchema = null) {
        SchemaId = schemaId;
        SchemaIdBase64 = schemaIdBase64;
        Version = version;
        Fields = fields;
        BaseSchema = baseSchema;
    }

    public string SchemaId { get; }

    public string SchemaIdBase64 { get; }

    public int Version { get; }

    public IReadOnlyList<SnapshotField> Fields { get; }

    public SnapshotKey? BaseSchema { get; }

    public SnapshotKey Key => new(SchemaId, Version);

    public bool ShapeEquals(SnapshotRecord other) {
        return BaseSchema == other.BaseSchema && Fields.SequenceEqual(other.Fields);
    }
}

internal readonly record struct SnapshotKey(string SchemaId, int Version);

internal readonly record struct SnapshotField(int FieldId, int TypeTag);

internal readonly record struct SnapshotHistoryResult(string Message);

internal sealed class SnapshotHistoryException : Exception {
    public SnapshotHistoryException(string message)
        : base(message) {
    }
}
