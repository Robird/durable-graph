using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Atelia.DurableGraph.SchemaHistory;
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
        public SchemaReference(string schemaId, int version, TypePattern? type = null) {
            SchemaId = schemaId;
            Version = version;
            Type = type ?? TypePattern.Named(schemaId);
        }

        public string SchemaId { get; }
        public int Version { get; }
        public TypePattern Type { get; }
    }

    private static bool HasDurableTypeShape(
        INamedTypeSymbol type,
        System.Threading.CancellationToken cancellationToken) {
        if ((type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct && type.TypeKind != TypeKind.Enum) || type.IsRefLikeType || type.IsRecord || type.Arity > 32 ||
            type.ContainingType is not null || type.DeclaringSyntaxReferences.Length == 0) {
            return false;
        }

        foreach (SyntaxReference syntaxReference in type.DeclaringSyntaxReferences) {
            if (type.TypeKind == TypeKind.Enum) {
                if (syntaxReference.GetSyntax(cancellationToken) is not EnumDeclarationSyntax enumDeclaration || HasFileModifier(enumDeclaration.Modifiers)) return false;
                continue;
            }
            if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration ||
                !HasPartialModifier(declaration.Modifiers) || HasFileModifier(declaration.Modifiers)) {
                return false;
            }
        }

        foreach (ITypeParameterSymbol parameter in type.TypeParameters) {
            if (parameter.AllowsRefLikeType) return false;
        }

        return true;
    }

    private static bool ReportSchemaSupportNameCollisions(SourceProductionContext context, INamedTypeSymbol type) {
        bool hasErrors = false;
        foreach (string name in new[] { "GetSchema", SchemaHistoryCacheName }) {
            var members = type.GetMembers(name);
            if (type.Name == name || !members.IsEmpty) {
                context.ReportDiagnostic(Diagnostic.Create(
                    ExistingSchemaSupportMember,
                    members.IsEmpty ? GetSourceLocation(type) : GetSourceLocation(members[0]),
                    type.ToDisplayString(QualifiedNameFormat), name));
                hasErrors = true;
            }
        }

        return hasErrors;
    }

    private static List<DurableTypeModel> ValidateSchemaChains(
        SourceProductionContext context,
        List<DurableTypeModel> types) {
        List<DurableTypeModel> result = new(types.Count);
        Dictionary<ISymbol, int> heights = new(SymbolEqualityComparer.Default);
        foreach (DurableTypeModel type in types) {
            bool valid = ValidateCurrentDependency(type, types, new HashSet<ISymbol>(SymbolEqualityComparer.Default), heights, 0, out _);
            if (valid) {
                result.Add(type);
            } else {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidSchemaAncestry, GetSourceLocation(type.Symbol),
                    type.Symbol.ToDisplayString(QualifiedNameFormat),
                    "base and inline dependencies must form a supported source-type DAG of depth at most 256; class ancestry must end at DurableBase"));
            }
        }

        return result;
    }

    private static bool ValidateCurrentDependency(DurableTypeModel type, List<DurableTypeModel> types,
        HashSet<ISymbol> path, Dictionary<ISymbol, int> heights, int depth, out int height) {
        height = 1;
        if (depth >= 256 || !path.Add(type.Symbol)) return false;
        if (heights.TryGetValue(type.Symbol, out height)) {
            path.Remove(type.Symbol);
            return depth + height <= 256;
        }
        height = 1;
        if (!type.IsInline && !HasMetadataName(type.Symbol.BaseType, DurableBaseMetadataName)) {
            int index = types.FindIndex(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, type.Symbol.BaseType!.OriginalDefinition));
            if (index < 0 || types[index].IsInline || !ValidateCurrentDependency(types[index], types, path, heights, depth + 1, out int childHeight)) return false;
            height = Math.Max(height, childHeight + 1);
        }
        foreach (DurableFieldModel field in type.Fields) {
            if (!field.InlineSchema.HasValue) continue;
            int index = types.FindIndex(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, ((INamedTypeSymbol)(IsNullableValue(field.Symbol.Type) ? ((INamedTypeSymbol)field.Symbol.Type).TypeArguments[0] : field.Symbol.Type)).OriginalDefinition));
            if (index < 0 || !types[index].IsInline || !ValidateCurrentDependency(types[index], types, path, heights, depth + 1, out int childHeight)) return false;
            height = Math.Max(height, childHeight + 1);
        }
        path.Remove(type.Symbol);
        heights[type.Symbol] = height;
        return true;
    }

    private static SchemaReference? GetCurrentBaseReference(DurableTypeModel model) {
        INamedTypeSymbol type = model.Symbol;
        if (model.IsInline || HasMetadataName(type.BaseType, DurableBaseMetadataName)) {
            return null;
        }

        AttributeData attribute = GetAttribute(type.BaseType!.GetAttributes(), DurableTypeAttributeMetadataName)!;
        return new SchemaReference(
            (string)attribute.ConstructorArguments[0].Value!,
            (int)attribute.ConstructorArguments[1].Value!, GetNamedTypePattern(type.BaseType!, model.ListType, model.DictionaryType));
    }

    private static bool SameReference(SchemaReference? left, SchemaReference? right) {
        return left.HasValue == right.HasValue &&
            (!left.HasValue ||
                (StringComparer.Ordinal.Equals(left.Value.SchemaId, right!.Value.SchemaId) &&
                    left.Value.Type.Equals(right.Value.Type) &&
                    left.Value.Version == right.Value.Version));
    }

    private static bool HaveSameShape(SchemaHistoryModel left, SchemaHistoryModel right) {
        return left.Kind == right.Kind && left.Arity == right.Arity && SameReference(left.BaseSchema, right.BaseSchema) && HaveSameFields(left.Fields, right.Fields);
    }

    // Accepted history is checked by itself. Current candidates must never repair a missing historical ancestor.
    private static bool ValidateHistoryClosure(SourceProductionContext context, List<SchemaHistoryModel> history) {
        bool valid = ValidateTemplateNominalShapes(context, history);
        Dictionary<string, int> kinds = new(StringComparer.Ordinal);
        Dictionary<string, int> heights = new(StringComparer.Ordinal);
        foreach (SchemaHistoryModel entry in history) {
            if (kinds.TryGetValue(entry.SchemaId, out int kind) && kind != (entry.Kind * 64 + entry.Arity)) {
                ReportInvalidHistoryAncestry(context, entry, "a Schema family cannot change kind or arity");
                valid = false;
            }
            kinds[entry.SchemaId] = entry.Kind * 64 + entry.Arity;
            if (!ValidatePatternReferences(context, entry, history)) valid = false;
            if (!ValidateExactDependency(context, entry, history, new HashSet<string>(StringComparer.Ordinal), heights, 0, out _)) valid = false;
        }
        return valid;
    }

    private static string SchemaKey(SchemaHistoryModel shape) =>
        Convert.ToBase64String(StrictUtf8.GetBytes(shape.SchemaId)) + ":" + shape.Version.ToString(CultureInfo.InvariantCulture);

    private static bool ValidateExactDependency(SourceProductionContext context, SchemaHistoryModel entry,
        List<SchemaHistoryModel> available, HashSet<string> path, Dictionary<string, int> heights, int depth, out int height) {
        height = 1;
        string key = SchemaKey(entry);
        if (depth >= 256 || !path.Add(key)) {
            ReportInvalidHistoryAncestry(context, entry, "the exact dependency graph is cyclic or exceeds depth 256");
            return false;
        }
        if (heights.TryGetValue(key, out height)) {
            path.Remove(key);
            if (depth + height > 256) {
                ReportInvalidHistoryAncestry(context, entry, "the exact dependency graph exceeds depth 256");
                return false;
            }
            return true;
        }
        height = 1;
        if (entry.Kind == 2 && entry.BaseSchema.HasValue) {
            ReportInvalidHistoryAncestry(context, entry, "inline Schema cannot have a base");
            return false;
        }
        HashSet<string> ancestorFamilies = new(StringComparer.Ordinal) { entry.SchemaId };
        SchemaReference? ancestor = entry.BaseSchema;
        while (ancestor.HasValue) {
            if (!ancestorFamilies.Add(ancestor.Value.SchemaId)) {
                ReportInvalidHistoryAncestry(context, entry, "an ancestor repeats a schema ID");
                return false;
            }
            List<SchemaHistoryModel> ancestors = FindHistory(available, ancestor.Value.SchemaId, ancestor.Value.Version);
            if (ancestors.Count == 0) break; // The exact dependency check below reports the missing record.
            ancestor = ancestors[0].BaseSchema;
        }
        List<(SchemaReference Reference, int Kind)> dependencies = new();
        if (entry.BaseSchema.HasValue) dependencies.Add((entry.BaseSchema.Value, 1));
        foreach (SchemaHistoryFieldModel field in entry.Fields) {
            if (field.InlineSchema.HasValue) dependencies.Add((field.InlineSchema.Value, 2));
        }
        foreach (var dependency in dependencies) {
            List<SchemaHistoryModel> matches = FindHistory(available, dependency.Reference.SchemaId, dependency.Reference.Version);
            if (matches.Count == 0) {
                ReportInvalidHistoryAncestry(context, entry,
                    (entry.Path.Length == 0 ? "current Schema is" : "accepted history is") +
                    " missing exact dependency '" + dependency.Reference.SchemaId + "' version " + dependency.Reference.Version.ToString(CultureInfo.InvariantCulture));
                return false;
            }
            if (!AllHaveSameShape(matches) || matches[0].Kind != dependency.Kind) {
                ReportInvalidHistoryAncestry(context, entry, "conflicting or wrong-kind exact dependency '" + dependency.Reference.SchemaId + "'");
                return false;
            }
            if (matches[0].Arity != dependency.Reference.Type.Arguments.Count) {
                ReportInvalidHistoryAncestry(context, entry, "wrong generic arity for exact dependency '" + dependency.Reference.SchemaId + "'");
                return false;
            }
            if (!ValidateExactDependency(context, matches[0], available, path, heights, depth + 1, out int childHeight)) return false;
            height = Math.Max(height, childHeight + 1);
        }
        path.Remove(key);
        heights[key] = height;
        return true;
    }

    private static void ReportInvalidHistoryAncestry(
        SourceProductionContext context, SchemaHistoryModel entry, string message) {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidSchemaAncestry, CreateAdditionalFileLocation(entry.Path), entry.SchemaId, message));
    }

    private static List<DurableTypeModel> GenerateSchemas(
        SourceProductionContext context,
        List<DurableTypeModel> currentTypes,
        List<SchemaHistoryModel> history) {
        List<SchemaHistoryModel> available = new(history);
        foreach (DurableTypeModel current in currentTypes) {
            available.RemoveAll(entry => entry.SchemaId == current.SchemaId && entry.Version == current.Version);
            available.Add(CurrentShape(current));
        }

        // Exact accepted records and current candidates must agree before a cache can share their keys.
        foreach (DurableTypeModel current in currentTypes) {
            foreach (SchemaHistoryModel old in history) {
                if (old.SchemaId == current.SchemaId && old.Kind != (current.IsInline ? 2 : 1)) {
                    context.ReportDiagnostic(Diagnostic.Create(CurrentSchemaHistoryMismatch, GetSourceLocation(current.Symbol),
                        current.Symbol.ToDisplayString(QualifiedNameFormat), current.SchemaId, current.Version));
                    return new List<DurableTypeModel>();
                }
            }
        }
        if (!ValidateHistoryClosure(context, available)) return new List<DurableTypeModel>();
        StringBuilder source = new("// <auto-generated/>\n#nullable enable\n");
        List<DurableTypeModel> validatedTypes = new();
        AppendSharedSchemaCache(source, available);
        bool emitted = false;
        foreach (DurableTypeModel current in currentTypes) {
            bool valid = true;
            List<SchemaHistoryModel> versions = new();
            for (int version = 1; version < current.Version; version++) {
                List<SchemaHistoryModel> matches = FindHistory(history, current.SchemaId, version);
                if (matches.Count == 0) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingSchemaHistory, GetSourceLocation(current.Symbol),
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

            SchemaHistoryModel currentShape = CurrentShape(current);
            List<SchemaHistoryModel> currentMatches = FindHistory(history, current.SchemaId, current.Version);
            if (currentMatches.Count > 0 &&
                (!AllHaveSameShape(currentMatches) || !HaveSameShape(currentMatches[0], currentShape))) {
                context.ReportDiagnostic(Diagnostic.Create(
                    CurrentSchemaHistoryMismatch, GetSourceLocation(current.Symbol),
                    current.Symbol.ToDisplayString(QualifiedNameFormat), current.SchemaId, current.Version));
                valid = false;
            }

            if (!valid) {
                continue;
            }

            versions.Add(currentShape);
            AppendSchemaType(source, current, versions, history, available);
            validatedTypes.Add(current);
            emitted = true;
        }

        if (emitted) {
            context.AddSource("DurableSchemas.g.cs", SourceText.From(source.ToString().Replace("\r\n", "\n"), Encoding.UTF8));
        }

        return validatedTypes;
    }

    private static SchemaHistoryModel CurrentShape(DurableTypeModel current) {
        return new SchemaHistoryModel(
            string.Empty, current.SchemaId, current.Version,
            ToSchemaHistoryFields(current.Fields), GetCurrentBaseReference(current), current.IsInline ? 2 : 1, current.Arity);
    }

    private static void AppendSchemaType(
        StringBuilder source,
        DurableTypeModel current,
        List<SchemaHistoryModel> versions,
        List<SchemaHistoryModel> history,
        List<SchemaHistoryModel> available) {
        bool hasNamespace = !current.Symbol.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace) {
            source.Append("namespace ").Append(current.Symbol.ContainingNamespace.ToDisplayString(QualifiedNameFormat)).AppendLine(" {");
        }

        string indent = hasNamespace ? "    " : string.Empty;
        string member = indent + "    ";
        string hiding = GetCurrentBaseReference(current).HasValue ? "new " : string.Empty;
        source.Append(indent).Append(current.IsInline ? "partial struct " : "partial class ").Append(EscapeIdentifier(current.Symbol.Name)).AppendLine(" {");
        source.Append(member).Append("public ").Append(hiding)
            .Append("static global::Atelia.DurableGraph.DurableSchema Schema => GetSchema(")
            .Append(current.Version.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        source.Append(member).Append("public ").Append(hiding)
            .AppendLine("static global::Atelia.DurableGraph.DurableSchema GetSchema(int version) => version switch {");
        foreach (SchemaHistoryModel version in versions) {
            string number = version.Version.ToString(CultureInfo.InvariantCulture);
            source.Append(member).Append("    ").Append(number).Append(" => ")
                .Append(SchemaHistoryCacheName).Append(".V").Append(number).AppendLine(",");
        }

        source.Append(member).AppendLine("    _ => throw new global::System.ArgumentOutOfRangeException(nameof(version)),");
        source.Append(member).AppendLine("};");
        source.Append(member).Append("private static class ").Append(SchemaHistoryCacheName).AppendLine(" {");
        foreach (SchemaHistoryModel version in versions) {
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

    private static string SchemaCacheMember(SchemaHistoryModel shape) =>
        "S_" + BitConverter.ToString(StrictUtf8.GetBytes(shape.SchemaId)).Replace("-", string.Empty) + "_V" + shape.Version.ToString(CultureInfo.InvariantCulture);

    private static void AppendSharedSchemaCache(StringBuilder source, List<SchemaHistoryModel> available) {
        source.AppendLine("namespace Atelia.DurableGraph.Generated {");
        source.AppendLine("internal static class __ExactSchemas {");
        HashSet<string> emitted = new(StringComparer.Ordinal);
        foreach (SchemaHistoryModel shape in available) {
            string member = SchemaCacheMember(shape);
            if (!emitted.Add(member)) continue;
            source.Append("    internal static global::Atelia.DurableGraph.DurableSchema ").Append(member).Append(" => Cache_").Append(member).AppendLine(".Value;");
            source.Append("    private static class Cache_").Append(member).AppendLine(" {");
            source.Append("        internal static readonly global::Atelia.DurableGraph.DurableSchema Value = ");
            AppendSchemaConstruction(source, shape, available);
            source.AppendLine(";");
            source.AppendLine("    }");
        }
        source.AppendLine("}");
        source.AppendLine("}");
    }

    private static void AppendSchemaExpression(StringBuilder source, SchemaHistoryModel shape, List<SchemaHistoryModel> available) {
        source.Append("global::Atelia.DurableGraph.Generated.__ExactSchemas.").Append(SchemaCacheMember(shape));
    }

    private static void AppendSchemaConstruction(
        StringBuilder source, SchemaHistoryModel shape, List<SchemaHistoryModel> available) {
        source.Append("new global::Atelia.DurableGraph.DurableSchema(")
            .Append(SymbolDisplay.FormatLiteral(shape.SchemaId, quote: true)).Append(", ")
            .Append(shape.Version.ToString(CultureInfo.InvariantCulture))
            .Append(", new global::Atelia.DurableGraph.DurableFieldInfo[] { ");
        foreach (SchemaHistoryFieldModel field in shape.Fields) {
            source.Append("new global::Atelia.DurableGraph.DurableFieldInfo(")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(", (global::Atelia.DurableGraph.TypeTag)")
                .Append(field.TypeTagValue.ToString(CultureInfo.InvariantCulture));
            if (field.TargetSchemaId is not null) {
                source.Append(", ").Append(SymbolDisplay.FormatLiteral(field.TargetSchemaId, quote: true));
            } else if (field.InlineSchema.HasValue) {
                SchemaReference reference = field.InlineSchema.Value;
                source.Append(", inlineSchema: ");
                AppendSchemaExpression(source, FindHistory(available, reference.SchemaId, reference.Version)[0], available);
            }
            source.Append("), ");
        }
        source.Append("}, ");
        if (shape.BaseSchema.HasValue) {
            SchemaReference reference = shape.BaseSchema.Value;
            AppendSchemaExpression(source, FindHistory(available, reference.SchemaId, reference.Version)[0], available);
        } else source.Append("null");
        source.Append(", (global::Atelia.DurableGraph.SchemaKind)").Append(shape.Kind.ToString(CultureInfo.InvariantCulture)).Append(')');
    }
}
