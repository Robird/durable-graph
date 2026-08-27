using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Generator;

/// <summary>
/// Generates the isolated R2 graph-operation probe. This generator is deliberately
/// internal and is not registered through <see cref="GeneratorAttribute"/>.
/// </summary>
internal sealed class DurableGraphOperationsProbeGenerator : IIncrementalGenerator {
    private const string DurableTypeAttributeMetadataName =
        "Atelia.DurableGraph.DurableTypeAttribute";
    private const string DurableFieldAttributeMetadataName =
        "Atelia.DurableGraph.DurableFieldAttribute";
    private const string TransientAttributeMetadataName =
        "Atelia.DurableGraph.TransientAttribute";
    private const string DurableBaseMetadataName =
        "Atelia.DurableGraph.DurableBase";
    private const string GeneratedGraphOperationsTypeName =
        "__DurableGraphOperations";
    private const string GeneratedHintName =
        "DurableGraphOperationsProbe.g.cs";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly DiagnosticDescriptor ExistingGraphOperationsMember = new(
        id: "DG0018",
        title: "Durable type has a reserved graph-operation member",
        messageFormat: "Type '{0}' already uses the reserved generated graph-operation name '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat FullyQualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<INamedTypeSymbol> durableTypes =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                DurableTypeAttributeMetadataName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) =>
                    (INamedTypeSymbol)attributeContext.TargetSymbol);

        context.RegisterSourceOutput(
            durableTypes.Collect(),
            static (productionContext, candidateTypes) =>
                GenerateGraphOperations(productionContext, candidateTypes));
    }

    private static void GenerateGraphOperations(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> candidateTypes) {
        List<INamedTypeSymbol> types = GetDistinctSortedTypes(candidateTypes);
        List<DurableGraphTypeModel> validTypes = new(types.Count);

        foreach (INamedTypeSymbol type in types) {
            context.CancellationToken.ThrowIfCancellationRequested();
            DurableGraphTypeModel? model = CreateTypeModel(context, type);

            if (model.HasValue) {
                validTypes.Add(model.Value);
            }
        }

        if (validTypes.Count == 0) {
            return;
        }

        string generatedSource = RenderSource(validTypes)
            .Replace("\r\n", "\n");
        context.AddSource(
            GeneratedHintName,
            SourceText.From(generatedSource, Encoding.UTF8));
    }

    private static List<INamedTypeSymbol> GetDistinctSortedTypes(
        ImmutableArray<INamedTypeSymbol> candidateTypes) {
        List<INamedTypeSymbol> result = new(candidateTypes.Length);

        foreach (INamedTypeSymbol candidate in candidateTypes) {
            bool alreadyAdded = false;

            foreach (INamedTypeSymbol existing in result) {
                if (SymbolEqualityComparer.Default.Equals(candidate, existing)) {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded) {
                result.Add(candidate);
            }
        }

        result.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            left.ToDisplayString(QualifiedNameFormat),
            right.ToDisplayString(QualifiedNameFormat)));
        return result;
    }

    private static DurableGraphTypeModel? CreateTypeModel(
        SourceProductionContext context,
        INamedTypeSymbol type) {
        string typeName = type.ToDisplayString(QualifiedNameFormat);

        if (!HasSupportedTypeShape(type, context.CancellationToken)) {
            context.ReportDiagnostic(Diagnostic.Create(
                DurableSchemaGenerator.InvalidTypeShape,
                GetSourceLocation(type),
                typeName));
            return null;
        }

        bool hasErrors = false;
        AttributeData? durableTypeAttribute = GetAttribute(
            type.GetAttributes(),
            DurableTypeAttributeMetadataName);

        if (durableTypeAttribute is null ||
            durableTypeAttribute.ConstructorArguments.Length != 2 ||
            durableTypeAttribute.ConstructorArguments[0].Value is not string schemaId ||
            string.IsNullOrWhiteSpace(schemaId) ||
            !CanEncodeStrictUtf8(schemaId) ||
            durableTypeAttribute.ConstructorArguments[1].Value is not int version ||
            version <= 0) {
            context.ReportDiagnostic(Diagnostic.Create(
                DurableSchemaGenerator.InvalidSchemaMetadata,
                GetAttributeLocation(durableTypeAttribute) ?? GetSourceLocation(type),
                typeName));
            hasErrors = true;
        }

        ImmutableArray<ISymbol> graphOperationMembers =
            type.GetMembers(GeneratedGraphOperationsTypeName);
        if (StringComparer.Ordinal.Equals(type.Name, GeneratedGraphOperationsTypeName) ||
            !graphOperationMembers.IsEmpty) {
            context.ReportDiagnostic(Diagnostic.Create(
                ExistingGraphOperationsMember,
                graphOperationMembers.IsEmpty
                    ? GetSourceLocation(type)
                    : GetSourceLocation(graphOperationMembers[0]),
                typeName,
                GeneratedGraphOperationsTypeName));
            hasErrors = true;
        }

        List<IFieldSymbol> fields = GetDirectFields(type);
        List<DurableGraphFieldModel> durableFields = new();

        foreach (IFieldSymbol field in fields) {
            AttributeData? durableFieldAttribute = GetAttribute(
                field.GetAttributes(),
                DurableFieldAttributeMetadataName);
            bool isDurable = durableFieldAttribute is not null;
            bool isTransient = HasAttribute(
                field.GetAttributes(),
                TransientAttributeMetadataName);

            if (field.IsStatic) {
                if (isDurable || isTransient) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DurableSchemaGenerator.ClassifiedStaticField,
                        GetSourceLocation(field),
                        field.Name));
                    hasErrors = true;
                }

                continue;
            }

            if (!isDurable && !isTransient) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.UnclassifiedField,
                    GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
                continue;
            }

            if (isDurable && isTransient) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.ConflictingFieldClassification,
                    GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
                continue;
            }

            if (isTransient) {
                continue;
            }

            if (field.IsReadOnly) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.ReadOnlyDurableField,
                    GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
            }

            if (durableFieldAttribute!.ConstructorArguments.Length != 1 ||
                durableFieldAttribute.ConstructorArguments[0].Value is not int fieldId ||
                fieldId <= 0) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.InvalidFieldId,
                    GetAttributeLocation(durableFieldAttribute) ?? GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
                continue;
            }

            if (!TryGetFieldKind(type, field, out DurableGraphFieldKind fieldKind)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.UnsupportedFieldType,
                    GetSourceLocation(field),
                    field.Name,
                    field.Type.ToDisplayString(QualifiedNameFormat)));
                hasErrors = true;
                continue;
            }

            durableFields.Add(new DurableGraphFieldModel(
                field,
                fieldId,
                fieldKind,
                field.Type.ToDisplayString(FullyQualifiedNameFormat)));
        }

        durableFields.Sort(static (left, right) => left.FieldId.CompareTo(right.FieldId));
        hasErrors |= ReportDuplicateFieldIds(context, typeName, durableFields);

        return hasErrors
            ? null
            : new DurableGraphTypeModel(type, durableFields);
    }

    private static bool HasSupportedTypeShape(
        INamedTypeSymbol type,
        System.Threading.CancellationToken cancellationToken) {
        if (type.TypeKind != TypeKind.Class ||
            type.IsRecord ||
            type.IsAbstract ||
            !type.IsSealed ||
            type.Arity != 0 ||
            type.ContainingType is not null ||
            !HasMetadataName(type.BaseType, DurableBaseMetadataName)) {
            return false;
        }

        foreach (SyntaxReference syntaxReference in type.DeclaringSyntaxReferences) {
            if (syntaxReference.GetSyntax(cancellationToken) is not ClassDeclarationSyntax declaration ||
                !HasPartialModifier(declaration.Modifiers) ||
                HasFileModifier(declaration.Modifiers)) {
                return false;
            }
        }

        return type.DeclaringSyntaxReferences.Length > 0;
    }

    private static List<IFieldSymbol> GetDirectFields(INamedTypeSymbol type) {
        List<IFieldSymbol> fields = new();

        foreach (ISymbol member in type.GetMembers()) {
            if (member is IFieldSymbol field && !field.IsImplicitlyDeclared) {
                fields.Add(field);
            }
        }

        fields.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        return fields;
    }

    private static bool ReportDuplicateFieldIds(
        SourceProductionContext context,
        string typeName,
        List<DurableGraphFieldModel> fields) {
        bool foundDuplicate = false;

        for (int start = 0; start < fields.Count;) {
            int end = start + 1;

            while (end < fields.Count && fields[end].FieldId == fields[start].FieldId) {
                end++;
            }

            if (end - start > 1) {
                foundDuplicate = true;
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableSchemaGenerator.DuplicateFieldId,
                    GetSourceLocation(fields[start + 1].Symbol),
                    fields[start].FieldId,
                    typeName));
            }

            start = end;
        }

        return foundDuplicate;
    }

    private static bool TryGetFieldKind(
        INamedTypeSymbol containingType,
        IFieldSymbol field,
        out DurableGraphFieldKind fieldKind) {
        switch (field.Type.SpecialType) {
            case SpecialType.System_Boolean:
                fieldKind = DurableGraphFieldKind.Boolean;
                return true;
            case SpecialType.System_Int32:
                fieldKind = DurableGraphFieldKind.Int32;
                return true;
            case SpecialType.System_Int64:
                fieldKind = DurableGraphFieldKind.Int64;
                return true;
            case SpecialType.System_String:
                fieldKind = DurableGraphFieldKind.String;
                return true;
        }

        if (SymbolEqualityComparer.Default.Equals(field.Type, containingType)) {
            fieldKind = DurableGraphFieldKind.SelfReference;
            return true;
        }

        fieldKind = default;
        return false;
    }

    private static string RenderSource(List<DurableGraphTypeModel> types) {
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");

        if (types.Count > 0) {
            source.AppendLine();
        }

        for (int index = 0; index < types.Count; index++) {
            AppendType(source, types[index]);

            if (index < types.Count - 1) {
                source.AppendLine();
            }
        }

        return source.ToString();
    }

    private static void AppendType(
        StringBuilder source,
        DurableGraphTypeModel model) {
        bool hasNamespace = !model.Symbol.ContainingNamespace.IsGlobalNamespace;
        string typeIndent = hasNamespace ? "    " : string.Empty;
        string memberIndent = typeIndent + "    ";
        string nestedMemberIndent = memberIndent + "    ";
        string fieldIndent = nestedMemberIndent + "    ";
        string statementIndent = fieldIndent + "    ";
        string fullyQualifiedTypeName =
            model.Symbol.ToDisplayString(FullyQualifiedNameFormat);

        if (hasNamespace) {
            source.Append("namespace ")
                .Append(model.Symbol.ContainingNamespace.ToDisplayString(QualifiedNameFormat))
                .AppendLine(" {");
        }

        source.Append(typeIndent)
            .Append("partial class ")
            .Append(EscapeIdentifier(model.Symbol.Name))
            .AppendLine(" {");
        source.Append(memberIndent)
            .Append("private static class ")
            .Append(GeneratedGraphOperationsTypeName)
            .AppendLine("<TIdentity>");
        source.Append(memberIndent)
            .AppendLine("    where TIdentity : struct {");

        AppendSnapshot(source, model, nestedMemberIndent, fieldIndent);
        source.AppendLine();
        AppendCapturedReferences(
            source,
            model,
            fullyQualifiedTypeName,
            nestedMemberIndent,
            fieldIndent);
        source.AppendLine();
        AppendCaptureCurrent(
            source,
            model,
            fullyQualifiedTypeName,
            nestedMemberIndent,
            fieldIndent,
            statementIndent);
        source.AppendLine();
        AppendDurableEquals(
            source,
            model,
            nestedMemberIndent,
            fieldIndent,
            statementIndent);
        source.AppendLine();
        AppendVisitReferences(
            source,
            model,
            fullyQualifiedTypeName,
            nestedMemberIndent,
            fieldIndent,
            statementIndent);

        source.Append(memberIndent).AppendLine("}");
        source.Append(typeIndent).AppendLine("}");

        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendSnapshot(
        StringBuilder source,
        DurableGraphTypeModel model,
        string nestedMemberIndent,
        string fieldIndent) {
        source.Append(nestedMemberIndent).AppendLine("public struct Snapshot {");

        foreach (DurableGraphFieldModel field in model.Fields) {
            source.Append(fieldIndent)
                .Append("public ")
                .Append(field.Kind == DurableGraphFieldKind.SelfReference
                    ? "TIdentity?"
                    : field.FieldTypeName)
                .Append(" Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
    }

    private static void AppendCapturedReferences(
        StringBuilder source,
        DurableGraphTypeModel model,
        string fullyQualifiedTypeName,
        string nestedMemberIndent,
        string fieldIndent) {
        source.Append(nestedMemberIndent)
            .AppendLine("public struct CapturedReferences {");

        foreach (DurableGraphFieldModel field in model.Fields) {
            if (field.Kind != DurableGraphFieldKind.SelfReference) {
                continue;
            }

            source.Append(fieldIndent)
                .Append("public ")
                .Append(fullyQualifiedTypeName)
                .Append("? Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
    }

    private static void AppendCaptureCurrent(
        StringBuilder source,
        DurableGraphTypeModel model,
        string fullyQualifiedTypeName,
        string nestedMemberIndent,
        string fieldIndent,
        string statementIndent) {
        source.Append(nestedMemberIndent).AppendLine("public static void CaptureCurrent(");
        source.Append(fieldIndent)
            .Append(fullyQualifiedTypeName)
            .AppendLine(" value,");
        source.Append(fieldIndent)
            .Append("global::System.Func<")
            .Append(fullyQualifiedTypeName)
            .AppendLine(", TIdentity> getIdentity,");
        source.Append(fieldIndent).AppendLine("out Snapshot snapshot,");
        source.Append(fieldIndent).AppendLine("out CapturedReferences references) {");
        source.Append(statementIndent)
            .AppendLine("if (value is null) {");
        source.Append(statementIndent)
            .AppendLine("    throw new global::System.ArgumentNullException(nameof(value));");
        source.Append(statementIndent).AppendLine("}");
        source.Append(statementIndent).AppendLine("if (getIdentity is null) {");
        source.Append(statementIndent)
            .AppendLine("    throw new global::System.ArgumentNullException(nameof(getIdentity));");
        source.Append(statementIndent).AppendLine("}");

        if (model.Fields.Count > 0) {
            source.AppendLine();
        }

        foreach (DurableGraphFieldModel field in model.Fields) {
            source.Append(statementIndent)
                .Append(field.Kind == DurableGraphFieldKind.SelfReference
                    ? fullyQualifiedTypeName + "?"
                    : field.FieldTypeName)
                .Append(" field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(" = value.")
                .Append(EscapeIdentifier(field.Symbol.Name))
                .AppendLine(";");
        }

        source.Append(statementIndent).AppendLine("snapshot = default;");

        if (model.Fields.Count > 0) {
            source.AppendLine();

            foreach (DurableGraphFieldModel field in model.Fields) {
                source.Append(statementIndent)
                    .Append("snapshot.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(" = ");

                if (field.Kind == DurableGraphFieldKind.SelfReference) {
                    source.Append("field")
                        .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                        .Append(" is null ? (TIdentity?)null : getIdentity(field")
                        .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(");");
                } else {
                    source.Append("field")
                        .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(";");
                }
            }
        }

        source.AppendLine();
        source.Append(statementIndent).AppendLine("references = default;");

        bool hasReferenceField = false;
        foreach (DurableGraphFieldModel field in model.Fields) {
            if (field.Kind != DurableGraphFieldKind.SelfReference) {
                continue;
            }

            if (!hasReferenceField) {
                source.AppendLine();
                hasReferenceField = true;
            }

            source.Append(statementIndent)
                .Append("references.Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(" = field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
    }

    private static void AppendDurableEquals(
        StringBuilder source,
        DurableGraphTypeModel model,
        string nestedMemberIndent,
        string fieldIndent,
        string statementIndent) {
        source.Append(nestedMemberIndent).AppendLine("public static bool DurableEquals(");
        source.Append(fieldIndent).AppendLine("in Snapshot left,");
        source.Append(fieldIndent).AppendLine("in Snapshot right) {");

        if (model.Fields.Count == 0) {
            source.Append(statementIndent).AppendLine("return true;");
            source.Append(nestedMemberIndent).AppendLine("}");
            return;
        }

        source.Append(statementIndent).AppendLine("return");

        for (int index = 0; index < model.Fields.Count; index++) {
            DurableGraphFieldModel field = model.Fields[index];
            source.Append(statementIndent).Append("    ");

            if (field.Kind == DurableGraphFieldKind.String) {
                source.Append("global::System.StringComparer.Ordinal.Equals(")
                    .Append("left.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(", right.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(')');
            } else if (field.Kind == DurableGraphFieldKind.SelfReference) {
                source.Append("global::System.Collections.Generic.EqualityComparer<TIdentity?>.Default.Equals(")
                    .Append("left.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(", right.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(')');
            } else {
                source.Append("left.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append(" == right.Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture));
            }

            source.AppendLine(index == model.Fields.Count - 1 ? ";" : " &&");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
    }

    private static void AppendVisitReferences(
        StringBuilder source,
        DurableGraphTypeModel model,
        string fullyQualifiedTypeName,
        string nestedMemberIndent,
        string fieldIndent,
        string statementIndent) {
        source.Append(nestedMemberIndent).AppendLine("public static void VisitReferences(");
        source.Append(fieldIndent).AppendLine("in CapturedReferences references,");
        source.Append(fieldIndent)
            .Append("global::System.Action<")
            .Append(fullyQualifiedTypeName)
            .AppendLine("> visitor) {");
        source.Append(statementIndent)
            .AppendLine("if (visitor is null) {");
        source.Append(statementIndent)
            .AppendLine("    throw new global::System.ArgumentNullException(nameof(visitor));");
        source.Append(statementIndent).AppendLine("}");

        foreach (DurableGraphFieldModel field in model.Fields) {
            if (field.Kind != DurableGraphFieldKind.SelfReference) {
                continue;
            }

            source.AppendLine();
            source.Append(statementIndent)
                .Append(fullyQualifiedTypeName)
                .Append("? field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(" = references.Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
            source.Append(statementIndent)
                .Append("if (field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" is not null) {");
            source.Append(statementIndent)
                .Append("    visitor(field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(");");
            source.Append(statementIndent).AppendLine("}");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
    }

    private static bool HasPartialModifier(SyntaxTokenList modifiers) {
        foreach (SyntaxToken modifier in modifiers) {
            if (modifier.IsKind(SyntaxKind.PartialKeyword)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasFileModifier(SyntaxTokenList modifiers) {
        foreach (SyntaxToken modifier in modifiers) {
            if (modifier.IsKind(SyntaxKind.FileKeyword)) {
                return true;
            }
        }

        return false;
    }

    private static bool CanEncodeStrictUtf8(string value) {
        try {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        } catch (EncoderFallbackException) {
            return false;
        }
    }

    private static string EscapeIdentifier(string identifier) {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
            SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }

    private static AttributeData? GetAttribute(
        ImmutableArray<AttributeData> attributes,
        string metadataName) {
        foreach (AttributeData attribute in attributes) {
            if (HasMetadataName(attribute.AttributeClass, metadataName)) {
                return attribute;
            }
        }

        return null;
    }

    private static bool HasAttribute(
        ImmutableArray<AttributeData> attributes,
        string metadataName) {
        return GetAttribute(attributes, metadataName) is not null;
    }

    private static bool HasMetadataName(
        INamedTypeSymbol? type,
        string metadataName) {
        if (type is null || type.ContainingType is not null) {
            return false;
        }

        int separator = metadataName.LastIndexOf('.');
        string expectedNamespace = separator < 0
            ? string.Empty
            : metadataName.Substring(0, separator);
        string expectedTypeName = separator < 0
            ? metadataName
            : metadataName.Substring(separator + 1);

        return StringComparer.Ordinal.Equals(type.MetadataName, expectedTypeName) &&
            StringComparer.Ordinal.Equals(
                type.ContainingNamespace.ToDisplayString(),
                expectedNamespace);
    }

    private static Location? GetAttributeLocation(AttributeData? attribute) {
        return attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation();
    }

    private static Location GetSourceLocation(ISymbol symbol) {
        foreach (Location location in symbol.Locations) {
            if (location.IsInSource) {
                return location;
            }
        }

        return Location.None;
    }

    private enum DurableGraphFieldKind {
        Boolean,
        Int32,
        Int64,
        String,
        SelfReference,
    }

    private readonly struct DurableGraphFieldModel {
        internal DurableGraphFieldModel(
            IFieldSymbol symbol,
            int fieldId,
            DurableGraphFieldKind kind,
            string fieldTypeName) {
            Symbol = symbol;
            FieldId = fieldId;
            Kind = kind;
            FieldTypeName = fieldTypeName;
        }

        internal IFieldSymbol Symbol { get; }

        internal int FieldId { get; }

        internal DurableGraphFieldKind Kind { get; }

        internal string FieldTypeName { get; }
    }

    private readonly struct DurableGraphTypeModel {
        internal DurableGraphTypeModel(
            INamedTypeSymbol symbol,
            List<DurableGraphFieldModel> fields) {
            Symbol = symbol;
            Fields = fields;
        }

        internal INamedTypeSymbol Symbol { get; }

        internal List<DurableGraphFieldModel> Fields { get; }
    }
}
