using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Atelia.DurableGraph.SchemaHistory {
    internal sealed class SchemaHistoryReferenceEntry {
        public SchemaHistoryReferenceEntry(string owner, string schemaId, int schemaVersion, int contractVersion, string manifest) {
            Owner = owner;
            SchemaId = schemaId;
            SchemaVersion = schemaVersion;
            ContractVersion = contractVersion;
            Manifest = manifest;
        }
        public string Owner { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public int ContractVersion { get; }
        public string Manifest { get; }
    }

    // Ephemeral build inputs; these records never become consumer-owned .dgschema files.
    internal static class SchemaHistoryReferenceProtocol {
        private const string Header = "// durable-graph-schema-references:1\n";
        private const string Prefix = "// reference|";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static string Render(IEnumerable<SchemaHistoryReferenceEntry> entries) {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var ordered = entries.OrderBy(entry => entry.Owner, StringComparer.Ordinal)
                .ThenBy(entry => entry.SchemaId, StringComparer.Ordinal).ThenBy(entry => entry.SchemaVersion).ToArray();
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var output = new StringBuilder(Header);
            foreach (var entry in ordered) {
                if (string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.SchemaId) || string.IsNullOrEmpty(entry.Manifest) ||
                    entry.SchemaVersion <= 0 || entry.ContractVersion != 1) {
                    throw new FormatException("Schema reference has an invalid owner, ID, version, manifest or execution contract.");
                }
                if (owners.TryGetValue(entry.SchemaId, out var owner) && owner != entry.Owner) {
                    throw new FormatException("Schema reference definition '" + entry.SchemaId + "' has multiple owners.");
                }
                owners[entry.SchemaId] = entry.Owner;
                if (!keys.Add(Encode(entry.SchemaId) + "|" + entry.SchemaVersion.ToString(CultureInfo.InvariantCulture))) {
                    throw new FormatException("Schema reference repeats an exact definition/version.");
                }
                output.Append(Prefix).Append(Encode(entry.Owner)).Append('|').Append(Encode(entry.SchemaId)).Append('|')
                    .Append(entry.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("|1|").Append(Encode(entry.Manifest)).Append('\n');
            }
            return output.ToString();
        }

        public static IReadOnlyList<SchemaHistoryReferenceEntry> Parse(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!text.StartsWith(Header, StringComparison.Ordinal) || !text.EndsWith("\n", StringComparison.Ordinal)) {
                throw new FormatException("Schema references require the version 1 header and canonical LF text.");
            }
            var records = new List<SchemaHistoryReferenceEntry>();
            var lines = text.Substring(Header.Length).Split('\n');
            for (int i = 0; i < lines.Length - 1; i++) {
                if (!lines[i].StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("Invalid schema reference entry.");
                var fields = lines[i].Substring(Prefix.Length).Split('|');
                if (fields.Length != 5) throw new FormatException("Invalid schema reference operands.");
                records.Add(new SchemaHistoryReferenceEntry(Decode(fields[0]), Decode(fields[1]), Number(fields[2]), Number(fields[3]), Decode(fields[4])));
            }
            if (!StringComparer.Ordinal.Equals(Render(records), text)) throw new FormatException("Schema references are not canonical or sorted.");
            return records;
        }

        public static string ComputeHash(string text) {
            using (var hash = SHA256.Create()) {
                var output = new StringBuilder(64);
                foreach (var value in hash.ComputeHash(Utf8.GetBytes(text))) output.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return output.ToString();
            }
        }

        private static string Encode(string value) => Convert.ToBase64String(Utf8.GetBytes(value));
        private static string Decode(string value) {
            var bytes = Convert.FromBase64String(value);
            if (Convert.ToBase64String(bytes) != value) throw new FormatException("Schema reference requires canonical Base64.");
            return Utf8.GetString(bytes);
        }
        private static int Number(string value) {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0 ||
                number.ToString(CultureInfo.InvariantCulture) != value) throw new FormatException("Schema reference requires a canonical positive Int32.");
            return number;
        }
    }
}
