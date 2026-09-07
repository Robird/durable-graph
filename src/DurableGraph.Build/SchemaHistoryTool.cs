using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atelia.DurableGraph.Build;

internal sealed class SchemaHistoryTool {
    private const string SchemaHistoryExtension = ".dgschema";

    public SchemaHistoryResult Publish(
        string manifestPath,
        string schemaHistoryDirectory) {
        IReadOnlyList<SchemaHistoryRecord> candidates = SchemaHistoryDocument.ParseManifest(manifestPath);

        if (candidates.Count == 0 && !Directory.Exists(schemaHistoryDirectory)) {
            return new SchemaHistoryResult(
                "published 0 schema-history record(s); 0 already exact");
        }

        Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord> existing = Directory.Exists(schemaHistoryDirectory)
            ? LoadHistory(schemaHistoryDirectory)
            : new Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord>();
        Dictionary<SchemaHistoryKey, SchemaHistoryRecord> available = existing.ToDictionary(
            pair => pair.Key, pair => pair.Value.Record);

        foreach (SchemaHistoryRecord candidate in candidates) {
            if (available.TryGetValue(candidate.Key, out SchemaHistoryRecord? historical) &&
                !candidate.ShapeEquals(historical)) {
                throw new SchemaHistoryException(
                    $"history conflicts with manifest for schema '{candidate.SchemaId}' version {candidate.Version}");
            }

            available[candidate.Key] = candidate;
        }

        ValidateClosure(available);
        List<PendingSchemaHistoryRecord> pending = new();
        int unchangedCount = 0;

        foreach (SchemaHistoryRecord candidate in candidates) {
            SchemaHistoryKey key = candidate.Key;

            if (existing.ContainsKey(key)) {
                unchangedCount++;
                continue;
            }

            string canonicalContent = SchemaHistoryDocument.RenderHistory(candidate);
            string fileName = SchemaHistoryDocument.GetHistoryFileName(candidate, canonicalContent);
            string destinationPath = Path.Combine(schemaHistoryDirectory, fileName);

            if (File.Exists(destinationPath)) {
                throw new SchemaHistoryException(
                    $"history destination '{fileName}' already exists but was not a valid indexed record");
            }

            pending.Add(new PendingSchemaHistoryRecord(destinationPath, canonicalContent));
        }

        Directory.CreateDirectory(schemaHistoryDirectory);

        foreach (PendingSchemaHistoryRecord record in pending) {
            PublishCreateOnly(record);
        }

        return new SchemaHistoryResult(
            $"published {pending.Count} schema-history record(s); {unchangedCount} already exact");
    }

    public SchemaHistoryResult Verify(
        string manifestPath,
        string schemaHistoryDirectory) {
        IReadOnlyList<SchemaHistoryRecord> candidates = SchemaHistoryDocument.ParseManifest(manifestPath);
        Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord> existing = Directory.Exists(schemaHistoryDirectory)
            ? LoadHistory(schemaHistoryDirectory)
            : new Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord>();

        foreach (SchemaHistoryRecord candidate in candidates) {
            if (!existing.TryGetValue(candidate.Key, out ExistingSchemaHistoryRecord? historical)) {
                throw new SchemaHistoryException(
                    $"history is missing schema '{candidate.SchemaId}' version {candidate.Version}");
            }

            if (!candidate.ShapeEquals(historical.Record)) {
                throw new SchemaHistoryException(
                    $"history conflicts with manifest for schema '{candidate.SchemaId}' version {candidate.Version}");
            }
        }

        return new SchemaHistoryResult(
            $"verified {candidates.Count} current manifest candidate(s) against {existing.Count} schema-history record(s)");
    }

    private static Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord> LoadHistory(
        string schemaHistoryDirectory) {
        if (Directory.EnumerateFiles(schemaHistoryDirectory, "*.dgsnapshot", SearchOption.TopDirectoryOnly).Any()) {
            throw new SchemaHistoryException(
                "DurableGraph Schema-history directory contains legacy .dgsnapshot files; " +
                "regenerate them as .dgschema because legacy history is not accepted.");
        }

        Dictionary<SchemaHistoryKey, ExistingSchemaHistoryRecord> records = new();
        string[] paths = Directory.GetFiles(
            schemaHistoryDirectory,
            $"*{SchemaHistoryExtension}",
            SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);

        foreach (string path in paths) {
            SchemaHistoryRecord record = SchemaHistoryDocument.ParseHistory(path);
            string canonicalContent = SchemaHistoryDocument.RenderHistory(record);
            string expectedFileName = SchemaHistoryDocument.GetHistoryFileName(record, canonicalContent);
            string actualFileName = Path.GetFileName(path);

            if (!StringComparer.Ordinal.Equals(actualFileName, expectedFileName)) {
                throw new SchemaHistoryException(
                    $"history file '{actualFileName}' must be named '{expectedFileName}'");
            }

            if (records.TryGetValue(record.Key, out ExistingSchemaHistoryRecord? duplicate)) {
                string conflict = record.ShapeEquals(duplicate.Record)
                    ? "duplicates"
                    : "conflicts with";
                throw new SchemaHistoryException(
                    $"history file '{actualFileName}' {conflict} '{duplicate.FileName}' for schema '{record.SchemaId}' version {record.Version}");
            }

            records.Add(
                record.Key,
                new ExistingSchemaHistoryRecord(actualFileName, record));
        }

        // Accepted history must close by itself. A current candidate cannot repair it.
        ValidateClosure(records.ToDictionary(pair => pair.Key, pair => pair.Value.Record));
        return records;
    }

    private static void ValidateClosure(IReadOnlyDictionary<SchemaHistoryKey, SchemaHistoryRecord> records) {
        foreach (SchemaHistoryRecord record in records.Values) {
            HashSet<string> ancestors = new(StringComparer.Ordinal) { record.SchemaId };
            SchemaHistoryRecord current = record;

            while (current.BaseSchema is SchemaHistoryKey baseKey) {
                if (!ancestors.Add(baseKey.SchemaId)) {
                    throw new SchemaHistoryException(
                        $"schema '{record.SchemaId}' version {record.Version} repeats ancestor schema '{baseKey.SchemaId}'");
                }

                if (!records.TryGetValue(baseKey, out SchemaHistoryRecord? baseSchema)) {
                    throw new SchemaHistoryException(
                        $"schema '{current.SchemaId}' version {current.Version} is missing base schema '{baseKey.SchemaId}' version {baseKey.Version}");
                }

                current = baseSchema;
            }
        }
    }

    private static void PublishCreateOnly(PendingSchemaHistoryRecord record) {
        string directory = Path.GetDirectoryName(record.DestinationPath)
            ?? throw new SchemaHistoryException("history destination has no directory");
        string fileName = Path.GetFileName(record.DestinationPath);
        string temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");

        try {
            byte[] bytes = SchemaHistoryDocument.Utf8NoBom.GetBytes(record.Content);

            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, record.DestinationPath, overwrite: false);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record ExistingSchemaHistoryRecord(string FileName, SchemaHistoryRecord Record);

    private sealed record PendingSchemaHistoryRecord(string DestinationPath, string Content);
}

internal static class SchemaHistoryDocument {
    private const string ManifestHeader = "// durable-graph-schema-history-manifest:1";
    private const string HistoryHeader = "// durable-graph-schema-history:1";
    private const string SchemaBegin = "// schema-begin";
    private const string SchemaEnd = "// schema-end";
    private const string SchemaIdPrefix = "// schema-id-base64:";
    private const string VersionPrefix = "// version:";
    private const string BasePrefix = "// base:";
    private const string FieldPrefix = "// field:";

    internal static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<SchemaHistoryRecord> ParseManifest(string path) {
        string text = ReadUtf8(path, allowByteOrderMark: true);
        IReadOnlyList<SchemaHistoryRecord> records = Parse(
            path,
            text,
            ManifestHeader,
            requireExactlyOneRecord: false);
        Dictionary<SchemaHistoryKey, SchemaHistoryRecord> distinct = new();

        foreach (SchemaHistoryRecord record in records) {
            if (distinct.TryGetValue(record.Key, out SchemaHistoryRecord? duplicate)) {
                string reason = record.ShapeEquals(duplicate)
                    ? "duplicates a record"
                    : "has a conflicting shape";
                throw Invalid(
                    path,
                    $"schema '{record.SchemaId}' version {record.Version} {reason}");
            }

            distinct.Add(record.Key, record);
        }

        return records;
    }

    public static SchemaHistoryRecord ParseHistory(string path) {
        byte[] bytes;

        try {
            bytes = File.ReadAllBytes(path);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            throw Invalid(path, exception.Message);
        }

        string text = DecodeUtf8(path, bytes, allowByteOrderMark: false);
        SchemaHistoryRecord record = Parse(
            path,
            text,
            HistoryHeader,
            requireExactlyOneRecord: true)[0];
        byte[] canonicalBytes = Utf8NoBom.GetBytes(RenderHistory(record));

        if (!bytes.AsSpan().SequenceEqual(canonicalBytes)) {
            throw Invalid(path, "content is not canonical UTF-8 with LF line endings");
        }

        return record;
    }

    public static string RenderHistory(SchemaHistoryRecord record) {
        StringBuilder builder = new();
        builder.AppendLine(HistoryHeader);
        AppendSchemaRecord(builder, record);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string GetHistoryFileName(
        SchemaHistoryRecord record,
        string canonicalContent) {
        string schemaHash = ToLowerHex(SHA256.HashData(Utf8NoBom.GetBytes(record.SchemaId)));
        string contentHash = ToLowerHex(SHA256.HashData(Utf8NoBom.GetBytes(canonicalContent)));
        return $"schema.{schemaHash}.V{record.Version.ToString(CultureInfo.InvariantCulture)}.{contentHash}.dgschema";
    }

    private static IReadOnlyList<SchemaHistoryRecord> Parse(
        string path,
        string text,
        string expectedHeader,
        bool requireExactlyOneRecord) {
        string[] lines = SplitLines(path, text);

        if (lines.Length == 0 || !StringComparer.Ordinal.Equals(lines[0], expectedHeader)) {
            throw Invalid(path, $"expected header '{expectedHeader}'");
        }

        List<SchemaHistoryRecord> records = new();
        int index = 1;

        while (index < lines.Length) {
            if (!StringComparer.Ordinal.Equals(lines[index], SchemaBegin)) {
                throw Invalid(path, $"line {index + 1} must be '{SchemaBegin}'");
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
            SchemaHistoryKey? baseSchema = null;

            if (index < lines.Length && lines[index].StartsWith(BasePrefix, StringComparison.Ordinal)) {
                string baseText = lines[index].Substring(BasePrefix.Length);
                int separator = baseText.IndexOf('|');

                if (separator <= 0 || separator != baseText.LastIndexOf('|')) {
                    throw Invalid(path, $"line {index + 1} has an invalid base entry");
                }

                baseSchema = new SchemaHistoryKey(
                    DecodeSchemaId(path, baseText.Substring(0, separator)),
                    ParsePositiveCanonicalInt(path, baseText.Substring(separator + 1), "base version"));
                index++;
            }

            List<SchemaHistoryField> fields = new();
            int previousFieldId = 0;

            while (index < lines.Length &&
                lines[index].StartsWith(FieldPrefix, StringComparison.Ordinal)) {
                string fieldText = lines[index].Substring(FieldPrefix.Length);
                string[] parts = fieldText.Split('|');

                if (parts.Length is < 2 or > 3) {
                    throw Invalid(path, $"line {index + 1} has an invalid field entry");
                }

                int fieldId = ParsePositiveCanonicalInt(
                    path,
                    parts[0],
                    $"field ID on line {index + 1}");
                int typeTag = ParsePositiveCanonicalInt(
                    path,
                    parts[1],
                    $"TypeTag on line {index + 1}");

                if (fieldId <= previousFieldId) {
                    throw Invalid(
                        path,
                        $"field IDs must be unique and sorted; line {index + 1} has {fieldId} after {previousFieldId}");
                }

                if (typeTag is < 1 or > 15) {
                    throw Invalid(path, $"line {index + 1} has unsupported TypeTag {typeTag}");
                }

                if (parts.Length != (typeTag == 15 ? 3 : 2)) {
                    throw Invalid(path, $"line {index + 1} has an invalid nominal reference operand");
                }
                string? targetSchemaId = typeTag == 15 ? DecodeSchemaId(path, parts[2]) : null;
                fields.Add(new SchemaHistoryField(fieldId, typeTag, targetSchemaId));
                previousFieldId = fieldId;
                index++;
            }

            if (index >= lines.Length || !StringComparer.Ordinal.Equals(lines[index], SchemaEnd)) {
                throw Invalid(path, $"record for schema '{schemaId}' version {version} has no '{SchemaEnd}'");
            }

            index++;
            records.Add(new SchemaHistoryRecord(schemaId, schemaIdBase64, version, fields, baseSchema));
        }

        if (requireExactlyOneRecord && records.Count != 1) {
            throw Invalid(path, $"history must contain exactly one record block, found {records.Count}");
        }

        return records;
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

    private static void AppendSchemaRecord(
        StringBuilder builder,
        SchemaHistoryRecord record) {
        builder.AppendLine(SchemaBegin);
        builder.Append(SchemaIdPrefix).AppendLine(record.SchemaIdBase64);
        builder.Append(VersionPrefix)
            .AppendLine(record.Version.ToString(CultureInfo.InvariantCulture));

        if (record.BaseSchema is SchemaHistoryKey baseSchema) {
            builder.Append(BasePrefix)
                .Append(Convert.ToBase64String(Utf8NoBom.GetBytes(baseSchema.SchemaId)))
                .Append('|')
                .AppendLine(baseSchema.Version.ToString(CultureInfo.InvariantCulture));
        }

        foreach (SchemaHistoryField field in record.Fields) {
            builder.Append(FieldPrefix)
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(field.TypeTag.ToString(CultureInfo.InvariantCulture));
            if (field.TargetSchemaId is not null) {
                builder.Append('|').Append(Convert.ToBase64String(Utf8NoBom.GetBytes(field.TargetSchemaId)));
            }
            builder.AppendLine();
        }

        builder.AppendLine(SchemaEnd);
    }

    private static string ToLowerHex(byte[] bytes) {
        return Convert.ToHexStringLower(bytes);
    }

    private static SchemaHistoryException Invalid(string path, string reason) {
        return new SchemaHistoryException(
            $"record file '{Path.GetFileName(path)}' is invalid: {reason}");
    }
}

internal sealed class SchemaHistoryRecord {
    public SchemaHistoryRecord(
        string schemaId,
        string schemaIdBase64,
        int version,
        IReadOnlyList<SchemaHistoryField> fields,
        SchemaHistoryKey? baseSchema = null) {
        SchemaId = schemaId;
        SchemaIdBase64 = schemaIdBase64;
        Version = version;
        Fields = fields;
        BaseSchema = baseSchema;
    }

    public string SchemaId { get; }

    public string SchemaIdBase64 { get; }

    public int Version { get; }

    public IReadOnlyList<SchemaHistoryField> Fields { get; }

    public SchemaHistoryKey? BaseSchema { get; }

    public SchemaHistoryKey Key => new(SchemaId, Version);

    public bool ShapeEquals(SchemaHistoryRecord other) {
        return BaseSchema == other.BaseSchema && Fields.SequenceEqual(other.Fields);
    }
}

internal readonly record struct SchemaHistoryKey(string SchemaId, int Version);

internal readonly record struct SchemaHistoryField(int FieldId, int TypeTag, string? TargetSchemaId = null);

internal readonly record struct SchemaHistoryResult(string Message);

internal sealed class SchemaHistoryException : Exception {
    public SchemaHistoryException(string message)
        : base(message) {
    }
}
