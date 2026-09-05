using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Generator;

public sealed partial class DurableSchemaGenerator {
    private const string SchemaHistoryCacheName = "__DurableSchemaHistory";

    private static readonly DiagnosticDescriptor InvalidSchemaAncestry = new(
        id: "DG0019",
        title: "Invalid durable schema ancestry",
        messageFormat: "Schema ancestry for '{0}' is invalid: {1}",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private readonly struct SchemaReference {
        public SchemaReference(string schemaId, int version) {
            SchemaId = schemaId;
            Version = version;
        }

        public string SchemaId { get; }
        public int Version { get; }
    }

    private static bool IsSchemaOnly(INamedTypeSymbol type) {
        AttributeData? attribute = GetAttribute(type.GetAttributes(), DurableTypeAttributeMetadataName);
        if (attribute is not null) {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments) {
                if (argument.Key == "SchemaOnly" && argument.Value.Value is true) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSchemaOnlyTypeShape(
        INamedTypeSymbol type,
        System.Threading.CancellationToken cancellationToken) {
        if (type.TypeKind != TypeKind.Class || type.IsRecord || type.Arity != 0 ||
            type.ContainingType is not null || type.DeclaringSyntaxReferences.Length == 0) {
            return false;
        }

        foreach (SyntaxReference syntaxReference in type.DeclaringSyntaxReferences) {
            if (syntaxReference.GetSyntax(cancellationToken) is not ClassDeclarationSyntax declaration ||
                !HasPartialModifier(declaration.Modifiers) || HasFileModifier(declaration.Modifiers)) {
                return false;
            }
        }

        return true;
    }

    private static bool ReportSchemaOnlyNameCollisions(SourceProductionContext context, INamedTypeSymbol type) {
        bool hasErrors = false;
        foreach (string name in new[] { "GetSchema", SchemaHistoryCacheName }) {
            var members = type.GetMembers(name);
            if (type.Name == name || !members.IsEmpty) {
                context.ReportDiagnostic(Diagnostic.Create(
                    ExistingSnapshotMember,
                    members.IsEmpty ? GetSourceLocation(type) : GetSourceLocation(members[0]),
                    type.ToDisplayString(QualifiedNameFormat), name));
                hasErrors = true;
            }
        }

        return hasErrors;
    }

    private static List<DurableTypeModel> ValidateSchemaOnlyChains(
        SourceProductionContext context,
        List<DurableTypeModel> types) {
        List<DurableTypeModel> result = new(types.Count);
        foreach (DurableTypeModel type in types) {
            if (!IsSchemaOnly(type.Symbol)) {
                result.Add(type);
                continue;
            }

            HashSet<ISymbol> seen = new(SymbolEqualityComparer.Default);
            INamedTypeSymbol? ancestor = type.Symbol;
            bool valid = true;
            while (!HasMetadataName(ancestor, DurableBaseMetadataName)) {
                if (ancestor is null || !seen.Add(ancestor) || !IsSchemaOnly(ancestor) ||
                    !SymbolEqualityComparer.Default.Equals(ancestor.ContainingAssembly, type.Symbol.ContainingAssembly) ||
                    !types.Exists(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, ancestor))) {
                    valid = false;
                    break;
                }

                ancestor = ancestor.BaseType;
            }

            if (valid) {
                result.Add(type);
            } else {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidSchemaAncestry, GetSourceLocation(type.Symbol),
                    type.Symbol.ToDisplayString(QualifiedNameFormat),
                    "every domain ancestor must explicitly use SchemaOnly, be a supported source type in this compilation, and end at DurableBase"));
            }
        }

        return result;
    }

    private static SchemaReference? GetCurrentBaseReference(INamedTypeSymbol type) {
        if (!IsSchemaOnly(type) || HasMetadataName(type.BaseType, DurableBaseMetadataName)) {
            return null;
        }

        AttributeData attribute = GetAttribute(type.BaseType!.GetAttributes(), DurableTypeAttributeMetadataName)!;
        return new SchemaReference(
            (string)attribute.ConstructorArguments[0].Value!,
            (int)attribute.ConstructorArguments[1].Value!);
    }

    private static bool SameReference(SchemaReference? left, SchemaReference? right) {
        return left.HasValue == right.HasValue &&
            (!left.HasValue ||
                (StringComparer.Ordinal.Equals(left.Value.SchemaId, right!.Value.SchemaId) &&
                    left.Value.Version == right.Value.Version));
    }

    private static bool HaveSameShape(SnapshotHistoryModel left, SnapshotHistoryModel right) {
        return SameReference(left.BaseSchema, right.BaseSchema) && HaveSameFields(left.Fields, right.Fields);
    }

    // Accepted history is checked by itself. Current candidates must never repair a missing historical ancestor.
    private static bool ValidateHistoryClosure(SourceProductionContext context, List<SnapshotHistoryModel> history) {
        bool valid = true;
        foreach (SnapshotHistoryModel entry in history) {
            HashSet<string> seen = new(StringComparer.Ordinal) { entry.SchemaId };
            SchemaReference? next = entry.BaseSchema;
            while (next.HasValue) {
                SchemaReference reference = next.Value;
                if (!seen.Add(reference.SchemaId)) {
                    ReportInvalidHistoryAncestry(context, entry, "an ancestor repeats a schema ID");
                    valid = false;
                    break;
                }

                List<SnapshotHistoryModel> matches = FindHistory(history, reference.SchemaId, reference.Version);
                if (matches.Count == 0) {
                    ReportInvalidHistoryAncestry(context, entry,
                        "accepted history is missing exact base '" + reference.SchemaId + "' version " +
                        reference.Version.ToString(CultureInfo.InvariantCulture));
                    valid = false;
                    break;
                }

                if (!AllHaveSameShape(matches)) {
                    valid = false; // ParseSnapshotHistory already diagnosed the conflicting key.
                    break;
                }

                next = matches[0].BaseSchema;
            }
        }

        return valid;
    }

    private static void ReportInvalidHistoryAncestry(
        SourceProductionContext context, SnapshotHistoryModel entry, string message) {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidSchemaAncestry, CreateAdditionalFileLocation(entry.Path), entry.SchemaId, message));
    }

    private static List<DurableTypeModel> GenerateSchemaOnly(
        SourceProductionContext context,
        List<DurableTypeModel> currentTypes,
        List<SnapshotHistoryModel> history) {
        List<SnapshotHistoryModel> available = new(history);
        foreach (DurableTypeModel current in currentTypes) {
            available.RemoveAll(entry => entry.SchemaId == current.SchemaId && entry.Version == current.Version);
            available.Add(CurrentShape(current));
        }

        StringBuilder source = new("// <auto-generated/>\n#nullable enable\n");
        List<DurableTypeModel> validatedTypes = new();
        bool emitted = false;
        foreach (DurableTypeModel current in currentTypes) {
            if (!IsSchemaOnly(current.Symbol)) {
                continue;
            }

            bool valid = true;
            List<SnapshotHistoryModel> versions = new();
            for (int version = 1; version < current.Version; version++) {
                List<SnapshotHistoryModel> matches = FindHistory(history, current.SchemaId, version);
                if (matches.Count == 0) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingSnapshotHistory, GetSourceLocation(current.Symbol),
                        current.Symbol.ToDisplayString(QualifiedNameFormat), current.SchemaId, version));
                    valid = false;
                    break;
                }

                if (!AllHaveSameShape(matches)) {
                    valid = false;
                    break;
                }

                versions.Add(matches[0]);
            }

            SnapshotHistoryModel currentShape = CurrentShape(current);
            List<SnapshotHistoryModel> currentMatches = FindHistory(history, current.SchemaId, current.Version);
            if (currentMatches.Count > 0 &&
                (!AllHaveSameShape(currentMatches) || !HaveSameShape(currentMatches[0], currentShape))) {
                context.ReportDiagnostic(Diagnostic.Create(
                    CurrentSnapshotMismatch, GetSourceLocation(current.Symbol),
                    current.Symbol.ToDisplayString(QualifiedNameFormat), current.SchemaId, current.Version));
                valid = false;
            }

            if (!valid) {
                continue;
            }

            versions.Add(currentShape);
            AppendSchemaOnlyType(source, current, versions, history, available);
            validatedTypes.Add(current);
            emitted = true;
        }

        if (emitted) {
            context.AddSource("DurableSchemaOnly.g.cs", SourceText.From(source.ToString().Replace("\r\n", "\n"), Encoding.UTF8));
        }

        return validatedTypes;
    }

    private static SnapshotHistoryModel CurrentShape(DurableTypeModel current) {
        return new SnapshotHistoryModel(
            string.Empty, current.SchemaId, current.Version,
            ToSnapshotFields(current.Fields), GetCurrentBaseReference(current.Symbol));
    }

    private static void AppendSchemaOnlyType(
        StringBuilder source,
        DurableTypeModel current,
        List<SnapshotHistoryModel> versions,
        List<SnapshotHistoryModel> history,
        List<SnapshotHistoryModel> available) {
        bool hasNamespace = !current.Symbol.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace) {
            source.Append("namespace ").Append(current.Symbol.ContainingNamespace.ToDisplayString(QualifiedNameFormat)).AppendLine(" {");
        }

        string indent = hasNamespace ? "    " : string.Empty;
        string member = indent + "    ";
        string hiding = GetCurrentBaseReference(current.Symbol).HasValue ? "new " : string.Empty;
        source.Append(indent).Append("partial class ").Append(EscapeIdentifier(current.Symbol.Name)).AppendLine(" {");
        source.Append(member).Append("public ").Append(hiding)
            .Append("static global::Atelia.DurableGraph.DurableSchema Schema => GetSchema(")
            .Append(current.Version.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        source.Append(member).Append("public ").Append(hiding)
            .AppendLine("static global::Atelia.DurableGraph.DurableSchema GetSchema(int version) => version switch {");
        foreach (SnapshotHistoryModel version in versions) {
            string number = version.Version.ToString(CultureInfo.InvariantCulture);
            source.Append(member).Append("    ").Append(number).Append(" => ")
                .Append(SchemaHistoryCacheName).Append(".V").Append(number).AppendLine(",");
        }

        source.Append(member).AppendLine("    _ => throw new global::System.ArgumentOutOfRangeException(nameof(version)),");
        source.Append(member).AppendLine("};");
        source.Append(member).Append("private static class ").Append(SchemaHistoryCacheName).AppendLine(" {");
        foreach (SnapshotHistoryModel version in versions) {
            source.Append(member).Append("    internal static readonly global::Atelia.DurableGraph.DurableSchema V")
                .Append(version.Version.ToString(CultureInfo.InvariantCulture)).Append(" = ");
            // Historical roots use only accepted records, even if a current candidate has the same key.
            AppendSchemaExpression(source, version, version.Version == current.Version ? available : history);
            source.AppendLine(";");
        }

        source.Append(member).AppendLine("}");
        source.Append(indent).AppendLine("}");
        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendSchemaExpression(
        StringBuilder source, SnapshotHistoryModel shape, List<SnapshotHistoryModel> available) {
        source.Append("new global::Atelia.DurableGraph.DurableSchema(")
            .Append(SymbolDisplay.FormatLiteral(shape.SchemaId, quote: true)).Append(", ")
            .Append(shape.Version.ToString(CultureInfo.InvariantCulture))
            .Append(", new global::Atelia.DurableGraph.DurableFieldInfo[] { ");
        foreach (SnapshotFieldModel field in shape.Fields) {
            source.Append("new global::Atelia.DurableGraph.DurableFieldInfo(")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(", (global::Atelia.DurableGraph.TypeTag)")
                .Append(field.TypeTagValue.ToString(CultureInfo.InvariantCulture)).Append("), ");
        }

        source.Append("}, ");
        if (shape.BaseSchema.HasValue) {
            SchemaReference reference = shape.BaseSchema.Value;
            AppendSchemaExpression(source, FindHistory(available, reference.SchemaId, reference.Version)[0], available);
        } else {
            source.Append("null");
        }

        source.Append(')');
    }
}
