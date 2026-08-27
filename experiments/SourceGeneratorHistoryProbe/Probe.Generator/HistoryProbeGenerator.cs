using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Experiments;

[Generator(LanguageNames.CSharp)]
public sealed class HistoryProbeGenerator : IIncrementalGenerator {
    private const string DurableAttributeMetadataName =
        "HistoryProbe.DurableAttribute";
    private const string FieldAttributeMetadataName =
        "HistoryProbe.FieldAttribute";

    private static readonly DiagnosticDescriptor InvalidSnapshot = new(
        id: "HP0001",
        title: "Invalid history probe snapshot",
        messageFormat: "History probe snapshot '{0}' is invalid: {1}",
        category: "HistoryProbe",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingSnapshot = new(
        id: "HP0002",
        title: "Conflicting history probe snapshot",
        messageFormat: "Schema '{0}' version {1} has conflicting snapshot shapes",
        category: "HistoryProbe",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<SnapshotModel?> currentSnapshots =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                DurableAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                    CreateCurrentSnapshot(attributeContext));

        IncrementalValuesProvider<SnapshotInput> historicalSnapshots =
            context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(
                    ".dgsnapshot",
                    StringComparison.OrdinalIgnoreCase))
                .Select(static (file, cancellationToken) =>
                    ParseHistoricalSnapshot(file, cancellationToken));

        IncrementalValueProvider<(
            ImmutableArray<SnapshotModel?> Current,
            ImmutableArray<SnapshotInput> Historical)> inputs =
                currentSnapshots.Collect().Combine(historicalSnapshots.Collect());

        context.RegisterSourceOutput(
            inputs,
            static (productionContext, input) => Emit(
                productionContext,
                input.Current,
                input.Historical));
    }

    private static SnapshotModel? CreateCurrentSnapshot(
        GeneratorAttributeSyntaxContext context) {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        if (attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not string schemaId ||
            string.IsNullOrWhiteSpace(schemaId) ||
            attribute.ConstructorArguments[1].Value is not int version ||
            version <= 0) {
            return null;
        }

        List<FieldModel> fields = new();

        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>()) {
            if (field.IsStatic || field.IsImplicitlyDeclared) {
                continue;
            }

            AttributeData? fieldAttribute = field.GetAttributes().FirstOrDefault(
                static candidate => candidate.AttributeClass?.ToDisplayString() ==
                    FieldAttributeMetadataName);

            if (fieldAttribute is null ||
                fieldAttribute.ConstructorArguments.Length != 1 ||
                fieldAttribute.ConstructorArguments[0].Value is not int fieldId ||
                fieldId <= 0 ||
                !TryGetFieldType(field.Type, out FieldType fieldType)) {
                return null;
            }

            fields.Add(new FieldModel(fieldId, fieldType));
        }

        fields.Sort(static (left, right) => left.Id.CompareTo(right.Id));

        if (fields.Count == 0 || fields.Select(static field => field.Id).Distinct().Count() != fields.Count) {
            return null;
        }

        return new SnapshotModel(schemaId, version, type.Name, fields.ToArray());
    }

    private static SnapshotInput ParseHistoricalSnapshot(
        AdditionalText file,
        System.Threading.CancellationToken cancellationToken) {
        SourceText? text = file.GetText(cancellationToken);

        if (text is null) {
            return SnapshotInput.Error(file.Path, "the file could not be read");
        }

        string? schemaId = null;
        string? typeName = null;
        int version = 0;
        List<FieldModel> fields = new();

        foreach (TextLine textLine in text.Lines) {
            string line = textLine.ToString();

            if (TryReadValue(line, "// schema-id:", out string value)) {
                schemaId = value;
            } else if (TryReadValue(line, "// type-name:", out value)) {
                typeName = value;
            } else if (TryReadValue(line, "// version:", out value)) {
                _ = int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out version);
            } else if (TryReadValue(line, "// field:", out value)) {
                string[] parts = value.Split('|');

                if (parts.Length != 2 ||
                    !int.TryParse(
                        parts[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int fieldId) ||
                    !Enum.TryParse(parts[1], ignoreCase: false, out FieldType fieldType)) {
                    return SnapshotInput.Error(file.Path, $"invalid field line '{line}'");
                }

                fields.Add(new FieldModel(fieldId, fieldType));
            }
        }

        fields.Sort(static (left, right) => left.Id.CompareTo(right.Id));

        if (string.IsNullOrWhiteSpace(schemaId) ||
            string.IsNullOrWhiteSpace(typeName) ||
            version <= 0 ||
            fields.Count == 0 ||
            fields.Any(static field => field.Id <= 0) ||
            fields.Select(static field => field.Id).Distinct().Count() != fields.Count) {
            return SnapshotInput.Error(file.Path, "required metadata is missing or invalid");
        }

        return SnapshotInput.Success(
            file.Path,
            new SnapshotModel(schemaId!, version, typeName!, fields.ToArray()));
    }

    private static bool TryReadValue(
        string line,
        string prefix,
        out string value) {
        if (line.StartsWith(prefix, StringComparison.Ordinal)) {
            value = line.Substring(prefix.Length).Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<SnapshotModel?> currentSnapshots,
        ImmutableArray<SnapshotInput> historicalSnapshots) {
        List<SnapshotModel> allSnapshots = new();
        List<SnapshotModel> current = new();

        foreach (SnapshotInput input in historicalSnapshots) {
            if (input.Model is null) {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidSnapshot,
                    Location.None,
                    input.Path,
                    input.ErrorMessage));
                continue;
            }

            allSnapshots.Add(input.Model);
        }

        foreach (SnapshotModel? snapshot in currentSnapshots) {
            if (snapshot is not null) {
                current.Add(snapshot);
                allSnapshots.Add(snapshot);
            }
        }

        List<SnapshotModel> distinctSnapshots = new();
        bool hasConflict = false;

        foreach (SnapshotModel snapshot in allSnapshots
            .OrderBy(static item => item.SchemaId, StringComparer.Ordinal)
            .ThenBy(static item => item.Version)) {
            SnapshotModel? existing = distinctSnapshots.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.SchemaId, snapshot.SchemaId) &&
                candidate.Version == snapshot.Version);

            if (existing is null) {
                distinctSnapshots.Add(snapshot);
            } else if (!existing.ShapeEquals(snapshot)) {
                hasConflict = true;
                context.ReportDiagnostic(Diagnostic.Create(
                    ConflictingSnapshot,
                    Location.None,
                    snapshot.SchemaId,
                    snapshot.Version));
            }
        }

        if (hasConflict || distinctSnapshots.Count == 0) {
            return;
        }

        context.AddSource(
            "HistoryProbeSnapshots.g.cs",
            SourceText.From(RenderSnapshots(distinctSnapshots), Encoding.UTF8));

        foreach (SnapshotModel snapshot in current) {
            context.AddSource(
                CandidateHintName(snapshot),
                SourceText.From(RenderCandidate(snapshot), Encoding.UTF8));
        }
    }

    private static string RenderSnapshots(IReadOnlyList<SnapshotModel> snapshots) {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace HistoryProbe.Generated;");
        builder.AppendLine();

        foreach (SnapshotModel snapshot in snapshots) {
            builder.Append("internal sealed class ")
                .Append(snapshot.TypeName)
                .Append("SnapshotV")
                .Append(snapshot.Version.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" {");

            foreach (FieldModel field in snapshot.Fields) {
                builder.Append("    public ")
                    .Append(FieldTypeName(field.Type))
                    .Append(" Field")
                    .Append(field.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(FieldInitializer(field.Type))
                    .AppendLine(";");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString().Replace("\r\n", "\n");
    }

    private static string RenderCandidate(SnapshotModel snapshot) {
        StringBuilder builder = new();
        builder.AppendLine("// history-probe-format:1");
        builder.Append("// schema-id:").AppendLine(snapshot.SchemaId);
        builder.Append("// type-name:").AppendLine(snapshot.TypeName);
        builder.Append("// version:")
            .AppendLine(snapshot.Version.ToString(CultureInfo.InvariantCulture));

        foreach (FieldModel field in snapshot.Fields) {
            builder.Append("// field:")
                .Append(field.Id.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .AppendLine(field.Type.ToString());
        }

        return builder.ToString().Replace("\r\n", "\n");
    }

    private static string CandidateHintName(SnapshotModel snapshot) {
        StringBuilder shape = new();

        foreach (FieldModel field in snapshot.Fields) {
            shape.Append(".F")
                .Append(field.Id.ToString(CultureInfo.InvariantCulture))
                .Append('-')
                .Append(((int)field.Type).ToString(CultureInfo.InvariantCulture));
        }

        return $"HistoryProbeSnapshot.{ToHex(snapshot.SchemaId)}.V{snapshot.Version}{shape}.dgsnapshot.cs";
    }

    private static string ToHex(string value) {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        StringBuilder builder = new(bytes.Length * 2);

        foreach (byte item in bytes) {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool TryGetFieldType(ITypeSymbol type, out FieldType fieldType) {
        switch (type.SpecialType) {
            case SpecialType.System_Boolean:
                fieldType = FieldType.Boolean;
                return true;
            case SpecialType.System_Int32:
                fieldType = FieldType.Int32;
                return true;
            case SpecialType.System_Int64:
                fieldType = FieldType.Int64;
                return true;
            case SpecialType.System_String:
                fieldType = FieldType.String;
                return true;
            default:
                fieldType = default;
                return false;
        }
    }

    private static string FieldTypeName(FieldType fieldType) {
        switch (fieldType) {
            case FieldType.Boolean:
                return "bool";
            case FieldType.Int32:
                return "int";
            case FieldType.Int64:
                return "long";
            case FieldType.String:
                return "string";
            default:
                throw new InvalidOperationException($"Unknown probe field type '{fieldType}'.");
        }
    }

    private static string FieldInitializer(FieldType fieldType) {
        return fieldType == FieldType.String ? " = string.Empty" : string.Empty;
    }

    private enum FieldType {
        Boolean = 1,
        Int32 = 2,
        Int64 = 3,
        String = 4,
    }

    private sealed class SnapshotInput {
        private SnapshotInput(
            string path,
            SnapshotModel? model,
            string? errorMessage) {
            Path = path;
            Model = model;
            ErrorMessage = errorMessage;
        }

        public string Path { get; }

        public SnapshotModel? Model { get; }

        public string? ErrorMessage { get; }

        public static SnapshotInput Success(string path, SnapshotModel model) {
            return new SnapshotInput(path, model, errorMessage: null);
        }

        public static SnapshotInput Error(string path, string errorMessage) {
            return new SnapshotInput(path, model: null, errorMessage);
        }
    }

    private sealed class SnapshotModel {
        public SnapshotModel(
            string schemaId,
            int version,
            string typeName,
            FieldModel[] fields) {
            SchemaId = schemaId;
            Version = version;
            TypeName = typeName;
            Fields = fields;
        }

        public string SchemaId { get; }

        public int Version { get; }

        public string TypeName { get; }

        public FieldModel[] Fields { get; }

        public bool ShapeEquals(SnapshotModel other) {
            if (!StringComparer.Ordinal.Equals(TypeName, other.TypeName) ||
                Fields.Length != other.Fields.Length) {
                return false;
            }

            for (int index = 0; index < Fields.Length; index++) {
                if (!Fields[index].Equals(other.Fields[index])) {
                    return false;
                }
            }

            return true;
        }
    }

    private readonly struct FieldModel : IEquatable<FieldModel> {
        public FieldModel(int id, FieldType type) {
            Id = id;
            Type = type;
        }

        public int Id { get; }

        public FieldType Type { get; }

        public bool Equals(FieldModel other) {
            return Id == other.Id && Type == other.Type;
        }

        public override bool Equals(object? obj) {
            return obj is FieldModel other && Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                return (Id * 397) ^ (int)Type;
            }
        }
    }
}
