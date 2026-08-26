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

[Generator(LanguageNames.CSharp)]
public sealed class DurableSchemaGenerator : IIncrementalGenerator {
    private const string DurableTypeAttributeMetadataName =
        "Atelia.DurableGraph.DurableTypeAttribute";
    private const string DurableFieldAttributeMetadataName =
        "Atelia.DurableGraph.DurableFieldAttribute";
    private const string TransientAttributeMetadataName =
        "Atelia.DurableGraph.TransientAttribute";
    private const string DurableBaseMetadataName =
        "Atelia.DurableGraph.DurableBase";
    private const string GeneratedSerializerTypeName = "__DurableSerializer";

    private static readonly DiagnosticDescriptor InvalidTypeShape = new(
        id: "DG0001",
        title: "Invalid durable type shape",
        messageFormat: "Type '{0}' must be a sealed, top-level, non-generic, non-record partial class that directly inherits Atelia.DurableGraph.DurableBase",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSchemaMetadata = new(
        id: "DG0002",
        title: "Invalid durable schema metadata",
        messageFormat: "Durable type '{0}' must declare a non-empty schema ID and a positive version",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnclassifiedField = new(
        id: "DG0003",
        title: "Unclassified durable type field",
        messageFormat: "Field '{0}' must be marked with exactly one of DurableFieldAttribute or TransientAttribute",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingFieldClassification = new(
        id: "DG0004",
        title: "Conflicting field classification",
        messageFormat: "Field '{0}' cannot be marked with both DurableFieldAttribute and TransientAttribute",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidFieldId = new(
        id: "DG0005",
        title: "Invalid durable field ID",
        messageFormat: "Durable field '{0}' must declare a positive field ID",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateFieldId = new(
        id: "DG0006",
        title: "Duplicate durable field ID",
        messageFormat: "Durable field ID {0} is duplicated in type '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedFieldType = new(
        id: "DG0007",
        title: "Unsupported durable field type",
        messageFormat: "Field '{0}' has unsupported durable type '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ExistingSchemaMember = new(
        id: "DG0008",
        title: "Durable type already has a Schema member",
        messageFormat: "Type '{0}' already declares a member named 'Schema'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ClassifiedStaticField = new(
        id: "DG0009",
        title: "Static field has durable classification",
        messageFormat: "Static field '{0}' cannot be marked as durable or transient",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ExistingSerializerMember = new(
        id: "DG0010",
        title: "Durable type has a reserved serializer member",
        messageFormat: "Type '{0}' already uses the reserved generated serializer name '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ReadOnlyDurableField = new(
        id: "DG0011",
        title: "Durable field is readonly",
        messageFormat: "Durable field '{0}' cannot be readonly because generated deserialization assigns it directly",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat FullyQualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<INamedTypeSymbol> durableTypes =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                DurableTypeAttributeMetadataName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) =>
                    (INamedTypeSymbol)attributeContext.TargetSymbol);

        context.RegisterSourceOutput(
            durableTypes.Collect(),
            static (productionContext, types) =>
                GenerateSchemas(productionContext, types));
    }

    private static void GenerateSchemas(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> candidateTypes) {
        List<INamedTypeSymbol> types = GetDistinctSortedTypes(candidateTypes);
        List<DurableTypeModel> validTypes = new(types.Count);

        foreach (INamedTypeSymbol type in types) {
            context.CancellationToken.ThrowIfCancellationRequested();
            DurableTypeModel? model = CreateTypeModel(context, type);

            if (model.HasValue) {
                validTypes.Add(model.Value);
            }
        }

        if (validTypes.Count > 0) {
            string generatedSource = RenderSource(validTypes)
                .Replace("\r\n", "\n");
            context.AddSource(
                "DurableSchemas.g.cs",
                SourceText.From(generatedSource, Encoding.UTF8));
        }
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

    private static DurableTypeModel? CreateTypeModel(
        SourceProductionContext context,
        INamedTypeSymbol type) {
        string typeName = type.ToDisplayString(QualifiedNameFormat);

        if (!HasSupportedTypeShape(type, context.CancellationToken)) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidTypeShape,
                GetSourceLocation(type),
                typeName));
            return null;
        }

        bool hasErrors = false;
        AttributeData? durableTypeAttribute = GetAttribute(
            type.GetAttributes(),
            DurableTypeAttributeMetadataName);
        string? schemaId = null;
        int version = 0;

        if (durableTypeAttribute is null ||
            durableTypeAttribute.ConstructorArguments.Length != 2 ||
            durableTypeAttribute.ConstructorArguments[0].Value is not string candidateSchemaId ||
            string.IsNullOrWhiteSpace(candidateSchemaId) ||
            durableTypeAttribute.ConstructorArguments[1].Value is not int candidateVersion ||
            candidateVersion <= 0) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidSchemaMetadata,
                GetAttributeLocation(durableTypeAttribute) ?? GetSourceLocation(type),
                typeName));
            hasErrors = true;
        } else {
            schemaId = candidateSchemaId;
            version = candidateVersion;
        }

        ImmutableArray<ISymbol> schemaMembers = type.GetMembers("Schema");
        if (StringComparer.Ordinal.Equals(type.Name, "Schema") ||
            !schemaMembers.IsEmpty) {
            context.ReportDiagnostic(Diagnostic.Create(
                ExistingSchemaMember,
                schemaMembers.IsEmpty
                    ? GetSourceLocation(type)
                    : GetSourceLocation(schemaMembers[0]),
                typeName));
            hasErrors = true;
        }

        hasErrors |= ReportReservedSerializerNameCollisions(
            context,
            type,
            typeName);

        List<IFieldSymbol> fields = GetDirectFields(type);
        List<DurableFieldModel> durableFields = new();

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
                        ClassifiedStaticField,
                        GetSourceLocation(field),
                        field.Name));
                    hasErrors = true;
                }

                continue;
            }

            if (!isDurable && !isTransient) {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnclassifiedField,
                    GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
                continue;
            }

            if (isDurable && isTransient) {
                context.ReportDiagnostic(Diagnostic.Create(
                    ConflictingFieldClassification,
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
                    ReadOnlyDurableField,
                    GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
            }

            int fieldId = 0;
            if (durableFieldAttribute!.ConstructorArguments.Length != 1 ||
                durableFieldAttribute.ConstructorArguments[0].Value is not int candidateFieldId ||
                candidateFieldId <= 0) {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidFieldId,
                    GetAttributeLocation(durableFieldAttribute) ?? GetSourceLocation(field),
                    field.Name));
                hasErrors = true;
                continue;
            }

            fieldId = candidateFieldId;

            if (!TryGetTypeTag(
                field.Type,
                out string? typeTag,
                out string? fieldTypeName)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedFieldType,
                    GetSourceLocation(field),
                    field.Name,
                    field.Type.ToDisplayString(QualifiedNameFormat)));
                hasErrors = true;
                continue;
            }

            durableFields.Add(new DurableFieldModel(
                field,
                fieldId,
                typeTag!,
                fieldTypeName!));
        }

        durableFields.Sort(static (left, right) => left.FieldId.CompareTo(right.FieldId));
        hasErrors |= ReportDuplicateFieldIds(context, typeName, durableFields);

        if (hasErrors) {
            return null;
        }

        return new DurableTypeModel(type, schemaId!, version, durableFields);
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

    private static bool ReportReservedSerializerNameCollisions(
        SourceProductionContext context,
        INamedTypeSymbol type,
        string typeName) {
        bool foundCollision = false;
        foundCollision |= ReportReservedSerializerNameCollision(
            context,
            type,
            typeName,
            "Serializer");
        foundCollision |= ReportReservedSerializerNameCollision(
            context,
            type,
            typeName,
            GeneratedSerializerTypeName);
        return foundCollision;
    }

    private static bool ReportReservedSerializerNameCollision(
        SourceProductionContext context,
        INamedTypeSymbol type,
        string typeName,
        string reservedName) {
        ImmutableArray<ISymbol> members = type.GetMembers(reservedName);
        bool typeNameCollides = StringComparer.Ordinal.Equals(type.Name, reservedName);

        if (members.IsEmpty && !typeNameCollides) {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ExistingSerializerMember,
            members.IsEmpty
                ? GetSourceLocation(type)
                : GetSourceLocation(members[0]),
            typeName,
            reservedName));
        return true;
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
        List<DurableFieldModel> fields) {
        bool foundDuplicate = false;

        for (int start = 0; start < fields.Count;) {
            int end = start + 1;

            while (end < fields.Count && fields[end].FieldId == fields[start].FieldId) {
                end++;
            }

            if (end - start > 1) {
                foundDuplicate = true;
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateFieldId,
                    GetSourceLocation(fields[start + 1].Symbol),
                    fields[start].FieldId,
                    typeName));
            }

            start = end;
        }

        return foundDuplicate;
    }

    private static bool TryGetTypeTag(
        ITypeSymbol type,
        out string? typeTag,
        out string? fieldTypeName) {
        switch (type.SpecialType) {
            case SpecialType.System_Boolean:
                typeTag = "Boolean";
                fieldTypeName = "global::System.Boolean";
                return true;
            case SpecialType.System_Int32:
                typeTag = "Int32";
                fieldTypeName = "global::System.Int32";
                return true;
            case SpecialType.System_Int64:
                typeTag = "Int64";
                fieldTypeName = "global::System.Int64";
                return true;
            case SpecialType.System_String:
                typeTag = "String";
                fieldTypeName = "global::System.String";
                return true;
            default:
                typeTag = null;
                fieldTypeName = null;
                return false;
        }
    }

    private static string RenderSource(List<DurableTypeModel> types) {
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

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
        DurableTypeModel model) {
        bool hasNamespace = !model.Symbol.ContainingNamespace.IsGlobalNamespace;
        string typeIndent = hasNamespace ? "    " : string.Empty;
        string memberIndent = typeIndent + "    ";
        string argumentIndent = memberIndent + "        ";
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
            .AppendLine("public static global::Atelia.DurableGraph.DurableSchema Schema { get; } =");
        source.Append(memberIndent)
            .Append("    new global::Atelia.DurableGraph.DurableSchema(")
            .AppendLine();
        source.Append(argumentIndent)
            .Append(SymbolDisplay.FormatLiteral(model.SchemaId, quote: true))
            .AppendLine(",");
        source.Append(argumentIndent)
            .Append(model.Version.ToString(CultureInfo.InvariantCulture))
            .Append(model.Fields.Count == 0 ? ");" : ",")
            .AppendLine();

        for (int index = 0; index < model.Fields.Count; index++) {
            DurableFieldModel field = model.Fields[index];
            source.Append(argumentIndent)
                .Append("new global::Atelia.DurableGraph.DurableFieldInfo(")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(", global::Atelia.DurableGraph.TypeTag.")
                .Append(field.TypeTag)
                .Append(index == model.Fields.Count - 1 ? "));" : "),")
                .AppendLine();
        }

        source.AppendLine();
        AppendSerializer(
            source,
            model,
            fullyQualifiedTypeName,
            memberIndent);

        source.Append(typeIndent).AppendLine("}");

        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendSerializer(
        StringBuilder source,
        DurableTypeModel model,
        string fullyQualifiedTypeName,
        string memberIndent) {
        string nestedMemberIndent = memberIndent + "    ";
        string statementIndent = nestedMemberIndent + "    ";
        string continuationIndent = statementIndent + "    ";
        const string ReadOnlyDictionaryType =
            "global::System.Collections.Generic.IReadOnlyDictionary<global::System.Int32, global::System.Object?>";
        const string DictionaryType =
            "global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.Object?>";

        source.Append(memberIndent)
            .Append("public static global::Atelia.DurableGraph.IDurableSerializer<")
            .Append(fullyQualifiedTypeName)
            .AppendLine("> Serializer { get; } =");
        source.Append(memberIndent)
            .Append("    new ")
            .Append(GeneratedSerializerTypeName)
            .AppendLine("();");
        source.AppendLine();
        source.Append(memberIndent)
            .Append("private sealed class ")
            .Append(GeneratedSerializerTypeName)
            .Append(" : global::Atelia.DurableGraph.IDurableSerializer<")
            .Append(fullyQualifiedTypeName)
            .AppendLine("> {");
        source.Append(nestedMemberIndent)
            .Append("public global::Atelia.DurableGraph.DurableSchema Schema => ")
            .Append(fullyQualifiedTypeName)
            .AppendLine(".Schema;");
        source.AppendLine();
        source.Append(nestedMemberIndent)
            .Append("public ")
            .Append(ReadOnlyDictionaryType)
            .Append(" Serialize(")
            .Append(fullyQualifiedTypeName)
            .AppendLine(" value) {");

        if (model.Fields.Count == 0) {
            source.Append(statementIndent)
                .Append("return new ")
                .Append(DictionaryType)
                .AppendLine("();");
        } else {
            source.Append(statementIndent)
                .Append("return new ")
                .Append(DictionaryType)
                .AppendLine(" {");

            foreach (DurableFieldModel field in model.Fields) {
                source.Append(continuationIndent)
                    .Append('[')
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append("] = value.")
                    .Append(EscapeIdentifier(field.Symbol.Name))
                    .AppendLine(",");
            }

            source.Append(statementIndent).AppendLine("};");
        }

        source.Append(nestedMemberIndent).AppendLine("}");
        source.AppendLine();
        source.Append(nestedMemberIndent)
            .Append("public ")
            .Append(fullyQualifiedTypeName)
            .Append(" Deserialize(")
            .Append(ReadOnlyDictionaryType)
            .AppendLine(" fields) {");
        source.Append(statementIndent)
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(fields);");
        source.Append(statementIndent)
            .Append(fullyQualifiedTypeName)
            .AppendLine(" value =");
        source.Append(continuationIndent)
            .Append('(')
            .Append(fullyQualifiedTypeName)
            .Append(")global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(")
            .AppendLine();
        source.Append(continuationIndent)
            .Append("    typeof(")
            .Append(fullyQualifiedTypeName)
            .AppendLine("));");

        foreach (DurableFieldModel field in model.Fields) {
            source.Append(statementIndent)
                .Append("value.")
                .Append(EscapeIdentifier(field.Symbol.Name))
                .Append(" = (")
                .Append(field.FieldTypeName)
                .Append(")fields[")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine("]!;");
        }

        source.Append(statementIndent).AppendLine("return value;");
        source.Append(nestedMemberIndent).AppendLine("}");
        source.Append(memberIndent).AppendLine("}");
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

    private readonly struct DurableFieldModel {
        public DurableFieldModel(
            IFieldSymbol symbol,
            int fieldId,
            string typeTag,
            string fieldTypeName) {
            Symbol = symbol;
            FieldId = fieldId;
            TypeTag = typeTag;
            FieldTypeName = fieldTypeName;
        }

        public IFieldSymbol Symbol { get; }

        public int FieldId { get; }

        public string TypeTag { get; }

        public string FieldTypeName { get; }
    }

    private readonly struct DurableTypeModel {
        public DurableTypeModel(
            INamedTypeSymbol symbol,
            string schemaId,
            int version,
            List<DurableFieldModel> fields) {
            Symbol = symbol;
            SchemaId = schemaId;
            Version = version;
            Fields = fields;
        }

        public INamedTypeSymbol Symbol { get; }

        public string SchemaId { get; }

        public int Version { get; }

        public List<DurableFieldModel> Fields { get; }
    }
}
