using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Generator;

public sealed partial class DurableSchemaGenerator {
    private const string BinaryBodyTypeName = "__DurableBinaryBody";
    private const string PayloadNamespace = "global::Atelia.DurableGraph.StateStore.Serialization.";

    private static readonly DiagnosticDescriptor InvalidBinaryBody = new(
        id: "DG0020",
        title: "Invalid durable binary body",
        messageFormat: "Binary body for '{0}' cannot be generated: {1}",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static bool RequestsBinaryBody(INamedTypeSymbol type) {
        AttributeData? attribute = GetAttribute(type.GetAttributes(), DurableTypeAttributeMetadataName);
        if (attribute is not null) {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments) {
                if (argument.Key == "GenerateBinaryBody" && argument.Value.Value is true) {
                    return true;
                }
            }
        }

        return false;
    }

    private static void GenerateBinaryBodies(
        SourceProductionContext context,
        List<DurableTypeModel> types,
        List<DurableTypeModel> validatedSchemaOnlyTypes,
        List<SnapshotHistoryModel> history,
        bool historyParsedSuccessfully) {
        // The metadata generator has already validated contiguous own versions and exact ancestry.
        // Historical layouts below must still resolve exclusively from accepted history.
        List<SnapshotHistoryModel> available = new(history);
        foreach (DurableTypeModel type in types) {
            available.RemoveAll(entry => entry.SchemaId == type.SchemaId && entry.Version == type.Version);
            available.Add(CurrentShape(type));
        }

        HashSet<ISymbol> eligible = new(SymbolEqualityComparer.Default);
        Dictionary<ISymbol, List<BinaryVersionModel>> layouts = new(SymbolEqualityComparer.Default);
        foreach (DurableTypeModel type in types) {
            if (!RequestsBinaryBody(type.Symbol)) {
                continue;
            }

            bool valid = true;
            if (!IsSchemaOnly(type.Symbol)) {
                ReportInvalidBinaryBody(context, type.Symbol, "GenerateBinaryBody requires SchemaOnly=true");
                valid = false;
            }

            var members = type.Symbol.GetMembers(BinaryBodyTypeName);
            if (type.Symbol.Name == BinaryBodyTypeName || !members.IsEmpty) {
                ReportInvalidBinaryBody(context, type.Symbol,
                    "the generated helper name '" + BinaryBodyTypeName + "' is reserved");
                valid = false;
            }

            foreach (DurableFieldModel field in type.Fields) {
                if (field.TypeTagValue != 1 && field.TypeTagValue != 2 && field.TypeTagValue != 3) {
                    ReportInvalidBinaryBody(context, type.Symbol,
                        "field '" + field.Symbol.Name + "' requires reference identity support; this body supports only bool, int and long",
                        GetSourceLocation(field.Symbol));
                    valid = false;
                }
            }

            if (valid && validatedSchemaOnlyTypes.Exists(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.Symbol, type.Symbol))) {
                // Parsing conflicts suppress all bodies before any accepted entry is selected.
                if (!historyParsedSuccessfully) {
                    continue;
                }

                List<BinaryVersionModel> versions = new();
                for (int version = 1; version <= type.Version; version++) {
                    bool isCurrent = version == type.Version;
                    SnapshotHistoryModel shape = isCurrent
                        ? CurrentShape(type)
                        : FindHistory(history, type.SchemaId, version)[0];
                    List<BinaryFieldModel> fields = new();
                    AppendBinaryFields(shape, isCurrent ? available : history, fields, 0);
                    foreach (BinaryFieldModel field in fields) {
                        if (field.TypeTagValue < 1 || field.TypeTagValue > 3) {
                            ReportInvalidBinaryBody(context, type.Symbol,
                                "version " + version.ToString(CultureInfo.InvariantCulture) +
                                " field '" + field.Name +
                                "' requires reference identity support; this body supports only bool, int and long");
                            valid = false;
                        }
                    }

                    versions.Add(new BinaryVersionModel(version, fields));
                    if (isCurrent) {
                        break; // Avoid overflowing an Int32.MaxValue version after the final iteration.
                    }
                }

                if (valid) {
                    eligible.Add(type.Symbol);
                    layouts.Add(type.Symbol, versions);
                }
            }
        }

        // Damaged input cannot reliably be attributed to one schema. Never expose a usable body from it.
        if (!historyParsedSuccessfully) {
            return;
        }

        StringBuilder source = new("// <auto-generated/>\n#nullable enable\n");
        bool emitted = false;
        foreach (DurableTypeModel type in types) {
            if (!eligible.Contains(type.Symbol)) {
                continue;
            }

            INamedTypeSymbol? ancestor = type.Symbol.BaseType;
            bool valid = true;
            while (!HasMetadataName(ancestor, DurableBaseMetadataName)) {
                if (ancestor is null || !eligible.Contains(ancestor)) {
                    ReportInvalidBinaryBody(context, type.Symbol,
                        "every domain ancestor must enable GenerateBinaryBody and pass metadata, history and binary body validation");
                    valid = false;
                    break;
                }

                ancestor = ancestor.BaseType;
            }

            if (valid) {
                AppendBinaryBodyType(source, type, layouts[type.Symbol]);
                emitted = true;
            }
        }

        if (emitted) {
            context.AddSource("DurableBinaryBodies.g.cs",
                SourceText.From(source.ToString().Replace("\r\n", "\n"), Encoding.UTF8));
        }
    }

    private static void ReportInvalidBinaryBody(
        SourceProductionContext context, INamedTypeSymbol type, string message, Location? location = null) {
        context.ReportDiagnostic(Diagnostic.Create(InvalidBinaryBody,
            location ?? GetSourceLocation(type), type.ToDisplayString(QualifiedNameFormat), message));
    }

    private static void AppendBinaryBodyType(
        StringBuilder source, DurableTypeModel type, List<BinaryVersionModel> versions) {
        bool hasNamespace = !type.Symbol.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace) {
            source.Append("namespace ").Append(type.Symbol.ContainingNamespace.ToDisplayString(QualifiedNameFormat)).AppendLine(" {");
        }

        string indent = hasNamespace ? "    " : string.Empty;
        string member = indent + "    ";
        bool hasDomainBase = GetCurrentBaseReference(type.Symbol).HasValue;
        source.Append(indent).Append("partial class ").Append(EscapeIdentifier(type.Symbol.Name)).AppendLine(" {");
        source.Append(member).Append("internal ").Append(hasDomainBase ? "new " : string.Empty)
            .Append("static class ").Append(BinaryBodyTypeName).AppendLine(" {");
        string bodyIndent = member + "    ";
        foreach (BinaryVersionModel version in versions) {
            AppendBinaryDto(source, type, version, bodyIndent);
            AppendBinaryDtoWrite(source, version, bodyIndent);
            AppendBinaryDtoRead(source, version, bodyIndent);
        }

        AppendBinaryCapture(source, type, versions[versions.Count - 1], bodyIndent, hasDomainBase);
        source.Append(member).AppendLine("}");
        source.Append(indent).AppendLine("}");
        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendBinaryCapture(
        StringBuilder source, DurableTypeModel type, BinaryVersionModel version, string indent, bool hasDomainBase) {
        source.Append(indent).Append("internal static ").Append(version.Name).Append(" Capture(")
            .Append(type.Symbol.ToDisplayString(FullyQualifiedNameFormat)).AppendLine(" value) {");
        source.Append(indent).AppendLine("    global::System.ArgumentNullException.ThrowIfNull(value);");
        if (hasDomainBase) {
            source.Append(indent).Append("    var baseState = ")
                .Append(type.Symbol.BaseType!.ToDisplayString(FullyQualifiedNameFormat))
                .Append('.').Append(BinaryBodyTypeName).AppendLine(".Capture(value);");
        }

        source.Append(indent).Append("    return new ").Append(version.Name).Append('(');
        int inheritedCount = version.Fields.Count - type.Fields.Count;
        for (int index = 0; index < version.Fields.Count; index++) {
            if (index > 0) {
                source.Append(", ");
            }

            if (index < inheritedCount) {
                source.Append("baseState.").Append(version.Fields[index].Name);
            } else {
                source.Append("value.").Append(EscapeIdentifier(type.Fields[index - inheritedCount].Symbol.Name));
            }
        }

        source.AppendLine(");");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryDto(
        StringBuilder source, DurableTypeModel type, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal readonly struct ").Append(version.Name).AppendLine(" {");
        foreach (BinaryFieldModel field in version.Fields) {
            TryGetFieldTypeName(field.TypeTagValue, out string? fieldType);
            source.Append(indent).Append("    internal readonly ").Append(fieldType).Append(' ')
                .Append(field.Name).AppendLine(";");
        }

        // An empty readonly struct uses its implicit parameterless constructor.
        if (version.Fields.Count > 0) {
            source.Append(indent).Append("    internal ").Append(version.Name).Append('(');
            for (int index = 0; index < version.Fields.Count; index++) {
                if (index > 0) {
                    source.Append(", ");
                }

                BinaryFieldModel field = version.Fields[index];
                TryGetFieldTypeName(field.TypeTagValue, out string? fieldType);
                source.Append(fieldType).Append(' ').Append(field.ParameterName);
            }

            source.AppendLine(") {");
            foreach (BinaryFieldModel field in version.Fields) {
                source.Append(indent).Append("        ").Append(field.Name).Append(" = ")
                    .Append(field.ParameterName).AppendLine(";");
            }

            source.Append(indent).AppendLine("    }");
        }

        source.Append(indent).Append("    internal static global::Atelia.DurableGraph.DurableSchema Schema => ")
            .Append(type.Symbol.ToDisplayString(FullyQualifiedNameFormat)).Append(".GetSchema(")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryDtoWrite(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static void Write(ref ").Append(PayloadNamespace)
            .Append("BinaryPayloadWriter writer, in ").Append(version.Name).AppendLine(" value) {");
        foreach (BinaryFieldModel field in version.Fields) {
            source.Append(indent).Append("    writer.Write").Append(GetTypeTagName(field.TypeTagValue))
                .Append("(value.").Append(field.Name).AppendLine(");");
        }

        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryDtoRead(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static ").Append(version.Name).Append(" Read").Append(version.Name)
            .Append("(ref ").Append(PayloadNamespace).AppendLine("BinaryPayloadReader reader) {");
        foreach (BinaryFieldModel field in version.Fields) {
            source.Append(indent).Append("    var ").Append(field.ParameterName).Append(" = reader.Read")
                .Append(GetTypeTagName(field.TypeTagValue)).AppendLine("();");
        }

        source.Append(indent).Append("    return new ").Append(version.Name).Append('(');
        for (int index = 0; index < version.Fields.Count; index++) {
            if (index > 0) {
                source.Append(", ");
            }

            source.Append(version.Fields[index].ParameterName);
        }

        source.AppendLine(");");
        source.Append(indent).AppendLine("}");
    }

    private static int AppendBinaryFields(
        SnapshotHistoryModel shape, List<SnapshotHistoryModel> available, List<BinaryFieldModel> fields, int segment) {
        if (shape.BaseSchema.HasValue) {
            SchemaReference reference = shape.BaseSchema.Value;
            segment = AppendBinaryFields(FindHistory(available, reference.SchemaId, reference.Version)[0],
                available, fields, segment);
        }

        foreach (SnapshotFieldModel field in shape.Fields) {
            fields.Add(new BinaryFieldModel(segment, field.FieldId, field.TypeTagValue));
        }

        return segment + 1;
    }

    private readonly struct BinaryVersionModel {
        public BinaryVersionModel(int version, List<BinaryFieldModel> fields) {
            Version = version;
            Fields = fields;
        }

        public int Version { get; }
        public string Name => "V" + Version.ToString(CultureInfo.InvariantCulture);
        public List<BinaryFieldModel> Fields { get; }
    }

    private readonly struct BinaryFieldModel {
        public BinaryFieldModel(int segment, int fieldId, int typeTagValue) {
            Name = "Segment" + segment.ToString(CultureInfo.InvariantCulture) +
                "Field" + fieldId.ToString(CultureInfo.InvariantCulture);
            TypeTagValue = typeTagValue;
        }

        public string Name { get; }
        public string ParameterName => "s" + Name.Substring(1);
        public int TypeTagValue { get; }
    }
}
