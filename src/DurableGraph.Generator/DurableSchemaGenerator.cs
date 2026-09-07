using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Generator;

[Generator(LanguageNames.CSharp)]
public sealed partial class DurableSchemaGenerator : IIncrementalGenerator {
    private const string DurableTypeAttributeMetadataName =
        "Atelia.DurableGraph.DurableTypeAttribute";
    private const string DurableFieldAttributeMetadataName =
        "Atelia.DurableGraph.DurableFieldAttribute";
    private const string TransientAttributeMetadataName =
        "Atelia.DurableGraph.TransientAttribute";
    private const string DurableBaseMetadataName =
        "Atelia.DurableGraph.DurableBase";
    private const string GeneratedSerializerTypeName = "__DurableSerializer";
    private const string SnapshotTypeNamePrefix = "__DurableSnapshotV";
    private const string SnapshotManifestHeader =
        "// durable-graph-snapshot-manifest:1";
    private const string SnapshotHistoryHeader =
        "// durable-graph-snapshot:1";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static readonly DiagnosticDescriptor InvalidTypeShape = new(
        id: "DG0001",
        title: "Invalid durable type shape",
        messageFormat: "Type '{0}' must be a top-level, non-generic, non-record partial class; legacy serializer types must also be sealed and directly inherit Atelia.DurableGraph.DurableBase",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidSchemaMetadata = new(
        id: "DG0002",
        title: "Invalid durable schema metadata",
        messageFormat: "Durable type '{0}' must declare a non-empty schema ID and a positive version",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnclassifiedField = new(
        id: "DG0003",
        title: "Unclassified durable type field",
        messageFormat: "Field '{0}' must be marked with exactly one of DurableFieldAttribute or TransientAttribute",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingFieldClassification = new(
        id: "DG0004",
        title: "Conflicting field classification",
        messageFormat: "Field '{0}' cannot be marked with both DurableFieldAttribute and TransientAttribute",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidFieldId = new(
        id: "DG0005",
        title: "Invalid durable field ID",
        messageFormat: "Durable field '{0}' must declare a positive field ID",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateFieldId = new(
        id: "DG0006",
        title: "Duplicate durable field ID",
        messageFormat: "Durable field ID {0} is duplicated in type '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedFieldType = new(
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

    internal static readonly DiagnosticDescriptor ClassifiedStaticField = new(
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

    internal static readonly DiagnosticDescriptor ReadOnlyDurableField = new(
        id: "DG0011",
        title: "Durable field is readonly",
        messageFormat: "Durable field '{0}' cannot be readonly because generated deserialization assigns it directly",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MalformedSnapshotHistory = new(
        id: "DG0012",
        title: "Malformed durable Schema history",
        messageFormat: "Schema history file '{0}' is malformed: {1}",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingSnapshotHistory = new(
        id: "DG0013",
        title: "Conflicting durable Schema history",
        messageFormat: "Schema history for schema '{0}' version {1} conflicts with another shape",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingSnapshotHistory = new(
        id: "DG0014",
        title: "Missing durable Schema history",
        messageFormat: "Durable type '{0}' requires Schema history for schema '{1}' version {2}",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CurrentSnapshotMismatch = new(
        id: "DG0015",
        title: "Current durable Schema-history candidate mismatch",
        messageFormat: "Durable type '{0}' does not match Schema history for schema '{1}' version {2}",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ExistingSnapshotMember = new(
        id: "DG0016",
        title: "Durable type has a reserved snapshot member",
        messageFormat: "Type '{0}' already uses the reserved generated snapshot name '{1}'",
        category: "DurableGraph.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateSchemaId = new(
        id: "DG0017",
        title: "Duplicate durable schema ID",
        messageFormat: "Durable schema ID '{0}' is used by both type '{1}' and type '{2}'",
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

        IncrementalValuesProvider<SnapshotText> snapshotHistoryFiles =
            context.AdditionalTextsProvider
                .Where(static file => StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetExtension(file.Path),
                    ".dgsnapshot"))
                .Select(static (file, cancellationToken) => new SnapshotText(
                    file.Path,
                    file.GetText(cancellationToken)?.ToString()));

        context.RegisterSourceOutput(
            durableTypes.Collect().Combine(snapshotHistoryFiles.Collect()).Combine(context.CompilationProvider),
            static (productionContext, input) =>
                GenerateSchemas(
                    productionContext,
                    input.Left.Left,
                    input.Left.Right,
                    input.Right));
    }

    private static void GenerateSchemas(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> candidateTypes,
        ImmutableArray<SnapshotText> snapshotHistoryFiles,
        Compilation compilation) {
        // Resolve from the actual core library, not a source-defined System.Half lookalike.
        INamedTypeSymbol? halfType = compilation.GetSpecialType(SpecialType.System_Object)
            .ContainingAssembly.GetTypeByMetadataName("System.Half");
        List<INamedTypeSymbol> types = GetDistinctSortedTypes(candidateTypes);
        List<DurableTypeModel> validTypes = new(types.Count);
        List<SnapshotHistoryModel> history = ParseSnapshotHistory(
            context,
            snapshotHistoryFiles,
            out bool historyParsedSuccessfully);

        foreach (INamedTypeSymbol type in types) {
            context.CancellationToken.ThrowIfCancellationRequested();
            DurableTypeModel? model = CreateTypeModel(context, type, halfType);

            if (model.HasValue) {
                validTypes.Add(model.Value);
            }
        }

        validTypes = RemoveDuplicateSchemaIds(context, validTypes);
        validTypes = ValidateSchemaOnlyChains(context, validTypes);

        if (!ValidateHistoryClosure(context, history)) {
            return;
        }

        if (validTypes.Count > 0 || types.Count == 0) {
            string manifestSource = RenderSnapshotManifest(validTypes)
                .Replace("\r\n", "\n");
            context.AddSource(
                "DurableGraphSnapshotCandidates.g.cs",
                SourceText.From(manifestSource, Encoding.UTF8));
        }

        if (validTypes.Count > 0) {
            List<DurableTypeModel> legacyTypes = validTypes.FindAll(static type => !IsSchemaOnly(type.Symbol));
            List<DurableSnapshotTypeModel> snapshotTypes =
                ValidateAndCreateSnapshotTypes(context, legacyTypes, history);

            List<DurableTypeModel> schemaOnlyTypes = GenerateSchemaOnly(context, validTypes, history);
            GenerateBinaryBodies(context, validTypes, schemaOnlyTypes, history, historyParsedSuccessfully);

            if (snapshotTypes.Count > 0) {
                string generatedSource = RenderSource(snapshotTypes)
                    .Replace("\r\n", "\n");
                context.AddSource(
                    "DurableSchemas.g.cs",
                    SourceText.From(generatedSource, Encoding.UTF8));

                string snapshotSource = RenderSnapshots(snapshotTypes)
                    .Replace("\r\n", "\n");
                context.AddSource(
                    "DurableSnapshots.g.cs",
                    SourceText.From(snapshotSource, Encoding.UTF8));
            }
        }
    }

    private static List<DurableTypeModel> RemoveDuplicateSchemaIds(
        SourceProductionContext context,
        List<DurableTypeModel> types) {
        List<DurableTypeModel> result = new(types.Count);

        for (int index = 0; index < types.Count; index++) {
            DurableTypeModel current = types[index];
            DurableTypeModel? conflicting = null;

            for (int candidateIndex = 0;
                candidateIndex < types.Count;
                candidateIndex++) {
                if (candidateIndex != index &&
                    StringComparer.Ordinal.Equals(
                        current.SchemaId,
                        types[candidateIndex].SchemaId)) {
                    conflicting = types[candidateIndex];
                    break;
                }
            }

            if (conflicting.HasValue) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateSchemaId,
                    GetSourceLocation(current.Symbol),
                    current.SchemaId,
                    current.Symbol.ToDisplayString(QualifiedNameFormat),
                    conflicting.Value.Symbol.ToDisplayString(QualifiedNameFormat)));
            } else {
                result.Add(current);
            }
        }

        return result;
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

    private static List<SnapshotHistoryModel> ParseSnapshotHistory(
        SourceProductionContext context,
        ImmutableArray<SnapshotText> files,
        out bool valid) {
        valid = true;
        List<SnapshotHistoryModel> history = new(files.Length);

        foreach (SnapshotText file in files) {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (TryParseSnapshotHistory(file, out SnapshotHistoryModel model, out string? error)) {
                history.Add(model);
            } else {
                valid = false;
                context.ReportDiagnostic(Diagnostic.Create(
                    MalformedSnapshotHistory,
                    CreateAdditionalFileLocation(file.Path),
                    file.Path,
                    error));
            }
        }

        history.Sort(static (left, right) => {
            int schemaComparison = StringComparer.Ordinal.Compare(
                left.SchemaId,
                right.SchemaId);

            if (schemaComparison != 0) {
                return schemaComparison;
            }

            int versionComparison = left.Version.CompareTo(right.Version);
            return versionComparison != 0
                ? versionComparison
                : StringComparer.Ordinal.Compare(left.Path, right.Path);
        });

        for (int index = 1; index < history.Count; index++) {
            SnapshotHistoryModel previous = history[index - 1];
            SnapshotHistoryModel current = history[index];

            if (StringComparer.Ordinal.Equals(previous.SchemaId, current.SchemaId) &&
                previous.Version == current.Version &&
                !HaveSameShape(previous, current)) {
                valid = false;
                context.ReportDiagnostic(Diagnostic.Create(
                    ConflictingSnapshotHistory,
                    CreateAdditionalFileLocation(current.Path),
                    current.SchemaId,
                    current.Version));
            }
        }

        return history;
    }

    private static bool TryParseSnapshotHistory(
        SnapshotText file,
        out SnapshotHistoryModel model,
        out string? error) {
        model = default;

        if (file.Content is null) {
            error = "the file could not be read";
            return false;
        }

        string normalized = file.Content.Replace("\r\n", "\n");
        if (normalized.IndexOf('\r') >= 0) {
            error = "bare carriage returns are not permitted";
            return false;
        }

        if (normalized.EndsWith("\n", StringComparison.Ordinal)) {
            normalized = normalized.Substring(0, normalized.Length - 1);
        }

        string[] lines = normalized.Split('\n');
        if (lines.Length < 5 ||
            !StringComparer.Ordinal.Equals(lines[0], SnapshotHistoryHeader) ||
            !StringComparer.Ordinal.Equals(lines[1], "// snapshot-begin") ||
            !StringComparer.Ordinal.Equals(lines[lines.Length - 1], "// snapshot-end")) {
            error = "the required header and single snapshot block were not found";
            return false;
        }

        const string SchemaPrefix = "// schema-id-base64:";
        if (!lines[2].StartsWith(SchemaPrefix, StringComparison.Ordinal) ||
            !TryDecodeSchemaId(lines[2].Substring(SchemaPrefix.Length), out string? schemaId)) {
            error = "the schema ID must be canonical base64-encoded non-empty UTF-8";
            return false;
        }

        const string VersionPrefix = "// version:";
        if (!lines[3].StartsWith(VersionPrefix, StringComparison.Ordinal) ||
            !TryParsePositiveCanonicalInt(
                lines[3].Substring(VersionPrefix.Length),
                out int version)) {
            error = "the version must be a positive canonical integer";
            return false;
        }

        List<SnapshotFieldModel> fields = new(lines.Length - 5);
        int previousFieldId = 0;
        SchemaReference? baseSchema = null;
        int firstFieldLine = 4;
        if (lines[4].StartsWith("// base:", StringComparison.Ordinal)) {
            string record = lines[4].Substring("// base:".Length);
            int separator = record.IndexOf('|');
            if (separator <= 0 || separator != record.LastIndexOf('|') ||
                !TryDecodeSchemaId(record.Substring(0, separator), out string? baseId) ||
                !TryParsePositiveCanonicalInt(record.Substring(separator + 1), out int baseVersion)) {
                error = "the base must contain a canonical UTF-8 base64 schema ID and positive canonical version";
                return false;
            }

            baseSchema = new SchemaReference(baseId!, baseVersion);
            firstFieldLine++;
        }

        for (int index = firstFieldLine; index < lines.Length - 1; index++) {
            const string FieldPrefix = "// field:";
            string line = lines[index];

            if (!line.StartsWith(FieldPrefix, StringComparison.Ordinal)) {
                error = "each field must use the canonical field record syntax";
                return false;
            }

            string fieldRecord = line.Substring(FieldPrefix.Length);
            string[] parts = fieldRecord.Split('|');
            string? targetSchemaId = null;
            if (parts.Length < 2 || parts.Length > 3 ||
                !TryParsePositiveCanonicalInt(
                    parts[0],
                    out int fieldId) ||
                fieldId <= previousFieldId ||
                !TryParsePositiveCanonicalInt(
                    parts[1],
                    out int typeTagValue) ||
                (typeTagValue == 15
                    ? parts.Length != 3 || !TryDecodeSchemaId(parts[2], out targetSchemaId)
                    : parts.Length != 2 || !TryGetFieldTypeName(typeTagValue, out _))) {
                error = "fields must have increasing positive IDs and supported numeric type tags";
                return false;
            }

            fields.Add(new SnapshotFieldModel(fieldId, typeTagValue, targetSchemaId));
            previousFieldId = fieldId;
        }

        model = new SnapshotHistoryModel(
            file.Path,
            schemaId!,
            version,
            fields,
            baseSchema);
        error = null;
        return true;
    }

    private static bool TryDecodeSchemaId(
        string encoded,
        out string? schemaId) {
        try {
            byte[] bytes = Convert.FromBase64String(encoded);
            if (!StringComparer.Ordinal.Equals(Convert.ToBase64String(bytes), encoded)) {
                schemaId = null;
                return false;
            }

            schemaId = StrictUtf8.GetString(bytes);
            return !string.IsNullOrWhiteSpace(schemaId);
        } catch (FormatException) {
            schemaId = null;
            return false;
        } catch (DecoderFallbackException) {
            schemaId = null;
            return false;
        }
    }

    private static bool TryParsePositiveCanonicalInt(
        string text,
        out int value) {
        return int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) &&
            value > 0 &&
            StringComparer.Ordinal.Equals(
                text,
                value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool CanEncodeStrictUtf8(string value) {
        try {
            StrictUtf8.GetByteCount(value);
            return true;
        } catch (EncoderFallbackException) {
            return false;
        }
    }

    private static Location CreateAdditionalFileLocation(string path) {
        return Location.Create(
            path,
            new TextSpan(0, 0),
            new LinePositionSpan(
                new LinePosition(0, 0),
                new LinePosition(0, 0)));
    }

    private static List<DurableSnapshotTypeModel> ValidateAndCreateSnapshotTypes(
        SourceProductionContext context,
        List<DurableTypeModel> currentTypes,
        List<SnapshotHistoryModel> history) {
        List<DurableSnapshotTypeModel> result = new(currentTypes.Count);

        foreach (DurableTypeModel currentType in currentTypes) {
            bool hasErrors = false;
            List<SnapshotVersionModel> versions = new();

            foreach (SnapshotHistoryModel entry in history) {
                if (StringComparer.Ordinal.Equals(entry.SchemaId, currentType.SchemaId) &&
                    (entry.BaseSchema.HasValue || entry.Fields.Exists(field => field.TypeTagValue == 15))) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidSchemaAncestry, GetSourceLocation(currentType.Symbol),
                        currentType.Symbol.ToDisplayString(QualifiedNameFormat),
                        "the legacy serializer cannot consume history with a base schema or durable references; use SchemaOnly"));
                    hasErrors = true;
                    break;
                }
            }

            for (int version = 1; version < currentType.Version; version++) {
                List<SnapshotHistoryModel> matches = FindHistory(
                    history,
                    currentType.SchemaId,
                    version);

                if (matches.Count == 0) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingSnapshotHistory,
                        GetSourceLocation(currentType.Symbol),
                        currentType.Symbol.ToDisplayString(QualifiedNameFormat),
                        currentType.SchemaId,
                        version));
                    hasErrors = true;
                    break;
                }

                if (!AllHaveSameShape(matches)) {
                    hasErrors = true;
                    break;
                }

                versions.Add(new SnapshotVersionModel(
                    version,
                    matches[0].Fields));
            }

            List<SnapshotHistoryModel> currentHistory = FindHistory(
                history,
                currentType.SchemaId,
                currentType.Version);
            List<SnapshotFieldModel> currentFields = ToSnapshotFields(
                currentType.Fields);

            if (currentHistory.Count > 0 &&
                (!AllHaveSameShape(currentHistory) ||
                    !HaveSameFields(currentHistory[0].Fields, currentFields) ||
                    currentHistory[0].BaseSchema.HasValue)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    CurrentSnapshotMismatch,
                    GetSourceLocation(currentType.Symbol),
                    currentType.Symbol.ToDisplayString(QualifiedNameFormat),
                    currentType.SchemaId,
                    currentType.Version));
                hasErrors = true;
            }

            versions.Add(new SnapshotVersionModel(
                currentType.Version,
                currentFields));

            foreach (SnapshotVersionModel version in versions) {
                string reservedName = SnapshotTypeNamePrefix +
                    version.Version.ToString(CultureInfo.InvariantCulture);
                ImmutableArray<ISymbol> members = currentType.Symbol.GetMembers(reservedName);

                if (!members.IsEmpty) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ExistingSnapshotMember,
                        GetSourceLocation(members[0]),
                        currentType.Symbol.ToDisplayString(QualifiedNameFormat),
                        reservedName));
                    hasErrors = true;
                }
            }

            if (!hasErrors) {
                result.Add(new DurableSnapshotTypeModel(
                    currentType,
                    versions));
            }
        }

        return result;
    }

    private static List<SnapshotHistoryModel> FindHistory(
        List<SnapshotHistoryModel> history,
        string schemaId,
        int version) {
        List<SnapshotHistoryModel> result = new();

        foreach (SnapshotHistoryModel candidate in history) {
            if (StringComparer.Ordinal.Equals(candidate.SchemaId, schemaId) &&
                candidate.Version == version) {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static bool AllHaveSameShape(List<SnapshotHistoryModel> history) {
        for (int index = 1; index < history.Count; index++) {
            if (!HaveSameShape(history[0], history[index])) {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameFields(
        List<SnapshotFieldModel> left,
        List<SnapshotFieldModel> right) {
        if (left.Count != right.Count) {
            return false;
        }

        for (int index = 0; index < left.Count; index++) {
            if (left[index].FieldId != right[index].FieldId ||
                left[index].TypeTagValue != right[index].TypeTagValue ||
                !StringComparer.Ordinal.Equals(left[index].TargetSchemaId, right[index].TargetSchemaId)) {
                return false;
            }
        }

        return true;
    }

    private static List<SnapshotFieldModel> ToSnapshotFields(
        List<DurableFieldModel> fields) {
        List<SnapshotFieldModel> result = new(fields.Count);

        foreach (DurableFieldModel field in fields) {
            result.Add(new SnapshotFieldModel(
                field.FieldId,
                field.TypeTagValue, field.TargetSchemaId));
        }

        return result;
    }

    private static DurableTypeModel? CreateTypeModel(
        SourceProductionContext context,
        INamedTypeSymbol type,
        INamedTypeSymbol? halfType) {
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
            !CanEncodeStrictUtf8(candidateSchemaId) ||
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

        hasErrors |= IsSchemaOnly(type)
            ? ReportSchemaOnlyNameCollisions(context, type)
            : ReportReservedSerializerNameCollisions(context, type, typeName);

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

            if (field.IsReadOnly && !(IsSchemaOnly(type) && RequestsBinaryBody(type))) {
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

            string? targetSchemaId = null;
            if (!TryGetTypeTag(
                field.Type,
                halfType,
                out string? typeTag,
                out int typeTagValue,
                out string? fieldTypeName) &&
                !(IsSchemaOnly(type) && TryGetNominalReference(field.Type, type,
                    context.CancellationToken, out typeTag, out typeTagValue, out fieldTypeName, out targetSchemaId))) {
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
                typeTagValue,
                fieldTypeName!, targetSchemaId));
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
        if (IsSchemaOnly(type)) {
            return HasSchemaOnlyTypeShape(type, cancellationToken);
        }

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
        INamedTypeSymbol? halfType,
        out string? typeTag,
        out int typeTagValue,
        out string? fieldTypeName) {
        typeTagValue = type.SpecialType switch {
            SpecialType.System_Boolean => 1,
            SpecialType.System_Int32 => 2,
            SpecialType.System_Int64 => 3,
            SpecialType.System_String => 4,
            SpecialType.System_Byte => 5,
            SpecialType.System_SByte => 6,
            SpecialType.System_Int16 => 7,
            SpecialType.System_UInt16 => 8,
            SpecialType.System_UInt32 => 9,
            SpecialType.System_UInt64 => 10,
            SpecialType.System_Char => 11,
            SpecialType.System_Single => 13,
            SpecialType.System_Double => 14,
            _ => halfType is not null && SymbolEqualityComparer.Default.Equals(type, halfType) ? 12 : 0,
        };
        if (TryGetFieldTypeName(typeTagValue, out fieldTypeName)) {
            typeTag = GetTypeTagName(typeTagValue);
            return true;
        }

        typeTag = null;
        return false;
    }

    private static bool TryGetNominalReference(
        ITypeSymbol fieldType, INamedTypeSymbol owner,
        System.Threading.CancellationToken cancellationToken,
        out string? typeTag, out int typeTagValue, out string? fieldTypeName, out string? targetSchemaId) {
        typeTag = null;
        typeTagValue = 0;
        fieldTypeName = null;
        targetSchemaId = null;
        if (fieldType is not INamedTypeSymbol target || !IsSchemaOnly(target) ||
            !HasSchemaOnlyTypeShape(target, cancellationToken) ||
            !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, owner.ContainingAssembly)) {
            return false;
        }
        AttributeData? attribute = GetAttribute(target.GetAttributes(), DurableTypeAttributeMetadataName);
        if (attribute is null || attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not string identity ||
            string.IsNullOrWhiteSpace(identity) || !CanEncodeStrictUtf8(identity)) {
            return false;
        }
        typeTag = "DurableReference";
        typeTagValue = 15;
        fieldTypeName = fieldType.ToDisplayString(QualifiedNameFormat);
        targetSchemaId = identity;
        return true;
    }

    private static bool TryGetFieldTypeName(
        int typeTagValue,
        out string? fieldTypeName) {
        if (typeTagValue >= 1 && typeTagValue <= 14) {
            fieldTypeName = "global::System." + GetTypeTagName(typeTagValue);
            return true;
        }

        fieldTypeName = null;
        return false;
    }

    private static string RenderSource(List<DurableSnapshotTypeModel> types) {
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

    private static string RenderSnapshotManifest(List<DurableTypeModel> types) {
        List<DurableTypeModel> sortedTypes = new(types);
        sortedTypes.Sort(static (left, right) => {
            int schemaComparison = StringComparer.Ordinal.Compare(
                left.SchemaId,
                right.SchemaId);

            if (schemaComparison != 0) {
                return schemaComparison;
            }

            int versionComparison = left.Version.CompareTo(right.Version);
            return versionComparison != 0
                ? versionComparison
                : StringComparer.Ordinal.Compare(
                    left.Symbol.ToDisplayString(QualifiedNameFormat),
                    right.Symbol.ToDisplayString(QualifiedNameFormat));
        });

        StringBuilder source = new();
        source.AppendLine(SnapshotManifestHeader);

        foreach (DurableTypeModel type in sortedTypes) {
            source.AppendLine("// snapshot-begin");
            source.Append("// schema-id-base64:")
                .AppendLine(Convert.ToBase64String(StrictUtf8.GetBytes(type.SchemaId)));
            source.Append("// version:")
                .AppendLine(type.Version.ToString(CultureInfo.InvariantCulture));
            SchemaReference? baseSchema = GetCurrentBaseReference(type.Symbol);
            if (baseSchema.HasValue) {
                source.Append("// base:")
                    .Append(Convert.ToBase64String(StrictUtf8.GetBytes(baseSchema.Value.SchemaId)))
                    .Append('|')
                    .AppendLine(baseSchema.Value.Version.ToString(CultureInfo.InvariantCulture));
            }

            foreach (DurableFieldModel field in type.Fields) {
                source.Append("// field:")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .Append('|')
                    .Append(field.TypeTagValue.ToString(CultureInfo.InvariantCulture));
                if (field.TargetSchemaId is not null) {
                    source.Append('|').Append(Convert.ToBase64String(StrictUtf8.GetBytes(field.TargetSchemaId)));
                }
                source.AppendLine();
            }

            source.AppendLine("// snapshot-end");
        }

        return source.ToString();
    }

    private static string RenderSnapshots(
        List<DurableSnapshotTypeModel> types) {
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        for (int index = 0; index < types.Count; index++) {
            AppendSnapshotType(source, types[index]);

            if (index < types.Count - 1) {
                source.AppendLine();
            }
        }

        return source.ToString();
    }

    private static void AppendSnapshotType(
        StringBuilder source,
        DurableSnapshotTypeModel model) {
        bool hasNamespace = !model.CurrentType.Symbol.ContainingNamespace.IsGlobalNamespace;
        string typeIndent = hasNamespace ? "    " : string.Empty;
        string memberIndent = typeIndent + "    ";
        string fieldIndent = memberIndent + "    ";

        if (hasNamespace) {
            source.Append("namespace ")
                .Append(model.CurrentType.Symbol.ContainingNamespace.ToDisplayString(QualifiedNameFormat))
                .AppendLine(" {");
        }

        source.Append(typeIndent)
            .Append("partial class ")
            .Append(EscapeIdentifier(model.CurrentType.Symbol.Name))
            .AppendLine(" {");

        for (int versionIndex = 0;
            versionIndex < model.Versions.Count;
            versionIndex++) {
            SnapshotVersionModel version = model.Versions[versionIndex];
            source.Append(memberIndent)
                .Append("private struct ")
                .Append(SnapshotTypeNamePrefix)
                .Append(version.Version.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" {");

            foreach (SnapshotFieldModel field in version.Fields) {
                TryGetFieldTypeName(field.TypeTagValue, out string? fieldTypeName);
                source.Append(fieldIndent)
                    .Append("public ")
                    .Append(fieldTypeName)
                    .Append(" Field")
                    .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(";");
            }

            source.Append(memberIndent).AppendLine("}");

            if (versionIndex < model.Versions.Count - 1) {
                source.AppendLine();
            }
        }

        for (int versionIndex = 0;
            versionIndex < model.Versions.Count - 1;
            versionIndex++) {
            int fromVersion = model.Versions[versionIndex].Version;
            int toVersion = model.Versions[versionIndex + 1].Version;
            source.AppendLine();
            source.Append(memberIndent)
                .Append("private static partial void UpgradeV")
                .Append(fromVersion.ToString(CultureInfo.InvariantCulture))
                .Append("ToV")
                .Append(toVersion.ToString(CultureInfo.InvariantCulture))
                .AppendLine("(");
            source.Append(fieldIndent)
                .Append("in ")
                .Append(SnapshotTypeNamePrefix)
                .Append(fromVersion.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" oldValue,");
            source.Append(fieldIndent)
                .Append("out ")
                .Append(SnapshotTypeNamePrefix)
                .Append(toVersion.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" newValue);");
        }

        source.Append(typeIndent).AppendLine("}");

        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendType(
        StringBuilder source,
        DurableSnapshotTypeModel snapshotModel) {
        DurableTypeModel model = snapshotModel.CurrentType;
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
            snapshotModel,
            fullyQualifiedTypeName,
            memberIndent);

        source.Append(typeIndent).AppendLine("}");

        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendSerializer(
        StringBuilder source,
        DurableSnapshotTypeModel snapshotModel,
        string fullyQualifiedTypeName,
        string memberIndent) {
        DurableTypeModel model = snapshotModel.CurrentType;
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

        foreach (SnapshotVersionModel version in snapshotModel.Versions) {
            source.AppendLine();
            AppendHistoricalSchema(
                source,
                model.SchemaId,
                version,
                nestedMemberIndent,
                statementIndent);
        }

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
            .AppendLine("global::Atelia.DurableGraph.DurableSchema storedSchema,");
        source.Append(statementIndent)
            .Append(ReadOnlyDictionaryType)
            .AppendLine(" fields) {");
        source.Append(statementIndent)
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(storedSchema);");
        source.Append(statementIndent)
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(fields);");
        source.Append(statementIndent)
            .AppendLine("if (!global::System.StringComparer.Ordinal.Equals(");
        source.Append(continuationIndent)
            .AppendLine("storedSchema.SchemaId,");
        source.Append(continuationIndent)
            .AppendLine("Schema.SchemaId)) {");
        source.Append(continuationIndent)
            .AppendLine("throw new global::Atelia.DurableGraph.StateSchemaMismatchException(");
        source.Append(continuationIndent)
            .AppendLine("    storedSchema,");
        source.Append(continuationIndent)
            .AppendLine("    Schema);");
        source.Append(statementIndent).AppendLine("}");
        source.AppendLine();

        foreach (SnapshotVersionModel version in snapshotModel.Versions) {
            source.Append(statementIndent)
                .Append(SnapshotTypeNamePrefix)
                .Append(version.Version.ToString(CultureInfo.InvariantCulture))
                .Append(" snapshotV")
                .Append(version.Version.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        source.AppendLine();
        source.Append(statementIndent).AppendLine("switch (storedSchema.Version) {");

        foreach (SnapshotVersionModel version in snapshotModel.Versions) {
            AppendDeserializeCase(
                source,
                version,
                continuationIndent);
        }

        source.Append(continuationIndent).AppendLine("default:");
        source.Append(continuationIndent)
            .AppendLine("    throw new global::Atelia.DurableGraph.UnsupportedSchemaVersionException(");
        source.Append(continuationIndent)
            .AppendLine("        storedSchema.SchemaId,");
        source.Append(continuationIndent)
            .AppendLine("        storedSchema.Version,");
        source.Append(continuationIndent)
            .AppendLine("        Schema.Version);");
        source.Append(statementIndent).AppendLine("}");

        for (int versionIndex = 0;
            versionIndex < snapshotModel.Versions.Count - 1;
            versionIndex++) {
            source.AppendLine();
            AppendUpgradeLabel(
                source,
                model.SchemaId,
                snapshotModel.Versions[versionIndex],
                snapshotModel.Versions[versionIndex + 1],
                fullyQualifiedTypeName,
                statementIndent,
                continuationIndent);
        }

        SnapshotVersionModel currentVersion =
            snapshotModel.Versions[snapshotModel.Versions.Count - 1];
        source.AppendLine();
        source.Append(statementIndent)
            .Append("SnapshotV")
            .Append(currentVersion.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Ready:");
        AppendMaterialization(
            source,
            model,
            currentVersion,
            fullyQualifiedTypeName,
            continuationIndent,
            continuationIndent + "    ");

        source.Append(nestedMemberIndent).AppendLine("}");
        source.Append(memberIndent).AppendLine("}");
    }

    private static void AppendHistoricalSchema(
        StringBuilder source,
        string schemaId,
        SnapshotVersionModel version,
        string memberIndent,
        string argumentIndent) {
        source.Append(memberIndent)
            .Append("private static global::Atelia.DurableGraph.DurableSchema SchemaV")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" { get; } =");
        source.Append(memberIndent)
            .AppendLine("    new global::Atelia.DurableGraph.DurableSchema(");
        source.Append(argumentIndent)
            .Append(SymbolDisplay.FormatLiteral(schemaId, quote: true))
            .AppendLine(",");
        source.Append(argumentIndent)
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .Append(version.Fields.Count == 0 ? ");" : ",")
            .AppendLine();

        for (int index = 0; index < version.Fields.Count; index++) {
            SnapshotFieldModel field = version.Fields[index];
            source.Append(argumentIndent)
                .Append("new global::Atelia.DurableGraph.DurableFieldInfo(")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(", global::Atelia.DurableGraph.TypeTag.")
                .Append(GetTypeTagName(field.TypeTagValue))
                .Append(index == version.Fields.Count - 1 ? "));" : "),")
                .AppendLine();
        }
    }

    private static void AppendDeserializeCase(
        StringBuilder source,
        SnapshotVersionModel version,
        string caseIndent) {
        string statementIndent = caseIndent + "    ";
        string nestedStatementIndent = statementIndent + "    ";
        string argumentIndent = nestedStatementIndent + "    ";
        source.Append(caseIndent)
            .Append("case ")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(":");
        source.Append(statementIndent)
            .Append("if (!storedSchema.Equals(SchemaV")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(")) {");
        source.Append(nestedStatementIndent)
            .AppendLine("throw new global::Atelia.DurableGraph.SchemaConflictException(");
        source.Append(argumentIndent)
            .AppendLine("storedSchema,");
        source.Append(argumentIndent)
            .Append("SchemaV")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(");");
        source.Append(statementIndent).AppendLine("}");
        AppendSnapshotDecodeAssignments(
            source,
            version,
            statementIndent);
        source.Append(statementIndent)
            .Append("goto SnapshotV")
            .Append(version.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Ready;");
    }

    private static void AppendUpgradeLabel(
        StringBuilder source,
        string schemaId,
        SnapshotVersionModel fromVersion,
        SnapshotVersionModel toVersion,
        string fullyQualifiedTypeName,
        string labelIndent,
        string statementIndent) {
        string continuationIndent = statementIndent + "    ";
        source.Append(labelIndent)
            .Append("SnapshotV")
            .Append(fromVersion.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Ready:");
        source.Append(statementIndent).AppendLine("try {");
        source.Append(continuationIndent)
            .Append(fullyQualifiedTypeName)
            .Append(".UpgradeV")
            .Append(fromVersion.Version.ToString(CultureInfo.InvariantCulture))
            .Append("ToV")
            .Append(toVersion.Version.ToString(CultureInfo.InvariantCulture))
            .Append("(in snapshotV")
            .Append(fromVersion.Version.ToString(CultureInfo.InvariantCulture))
            .Append(", out snapshotV")
            .Append(toVersion.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(");");
        source.Append(statementIndent)
            .AppendLine("} catch (global::System.Exception exception) {");
        source.Append(continuationIndent)
            .AppendLine("throw new global::Atelia.DurableGraph.DurableUpgradeException(");
        source.Append(continuationIndent)
            .Append("    ")
            .Append(SymbolDisplay.FormatLiteral(schemaId, quote: true))
            .AppendLine(",");
        source.Append(continuationIndent)
            .Append("    ")
            .Append(fromVersion.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(",");
        source.Append(continuationIndent)
            .Append("    ")
            .Append(toVersion.Version.ToString(CultureInfo.InvariantCulture))
            .AppendLine(",");
        source.Append(continuationIndent)
            .AppendLine("    exception);");
        source.Append(statementIndent).AppendLine("}");
    }

    private static void AppendMaterialization(
        StringBuilder source,
        DurableTypeModel model,
        SnapshotVersionModel currentVersion,
        string fullyQualifiedTypeName,
        string statementIndent,
        string continuationIndent) {
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
                .Append(" = snapshotV")
                .Append(currentVersion.Version.ToString(CultureInfo.InvariantCulture))
                .Append(".Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine(";");
        }

        source.Append(statementIndent).AppendLine("return value;");
    }

    private static void AppendSnapshotDecodeAssignments(
        StringBuilder source,
        SnapshotVersionModel version,
        string statementIndent) {
        if (version.Fields.Count == 0) {
            source.Append(statementIndent)
                .Append("snapshotV")
                .Append(version.Version.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" = default;");
            return;
        }

        foreach (SnapshotFieldModel field in version.Fields) {
            TryGetFieldTypeName(field.TypeTagValue, out string? fieldTypeName);
            source.Append(statementIndent)
                .Append("snapshotV")
                .Append(version.Version.ToString(CultureInfo.InvariantCulture))
                .Append(".Field")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .Append(" = (")
                .Append(fieldTypeName)
                .Append(")fields[")
                .Append(field.FieldId.ToString(CultureInfo.InvariantCulture))
                .AppendLine("]!;");
        }
    }

    private static string GetTypeTagName(int typeTagValue) {
        switch (typeTagValue) {
            case 1:
                return "Boolean";
            case 2:
                return "Int32";
            case 3:
                return "Int64";
            case 4:
                return "String";
            case 5:
                return "Byte";
            case 6:
                return "SByte";
            case 7:
                return "Int16";
            case 8:
                return "UInt16";
            case 9:
                return "UInt32";
            case 10:
                return "UInt64";
            case 11:
                return "Char";
            case 12:
                return "Half";
            case 13:
                return "Single";
            case 14:
                return "Double";
            case 15:
                return "DurableReference";
            default:
                throw new InvalidOperationException("Unsupported snapshot type tag.");
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

    private readonly struct DurableFieldModel {
        public DurableFieldModel(
            IFieldSymbol symbol,
            int fieldId,
            string typeTag,
            int typeTagValue,
            string fieldTypeName,
            string? targetSchemaId = null) {
            Symbol = symbol;
            FieldId = fieldId;
            TypeTag = typeTag;
            TypeTagValue = typeTagValue;
            FieldTypeName = fieldTypeName;
            TargetSchemaId = targetSchemaId;
        }

        public IFieldSymbol Symbol { get; }

        public int FieldId { get; }

        public string TypeTag { get; }

        public int TypeTagValue { get; }

        public string FieldTypeName { get; }
        public string? TargetSchemaId { get; }
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

    private readonly struct SnapshotText {
        public SnapshotText(string path, string? content) {
            Path = path;
            Content = content;
        }

        public string Path { get; }

        public string? Content { get; }
    }

    private readonly struct SnapshotFieldModel {
        public SnapshotFieldModel(int fieldId, int typeTagValue, string? targetSchemaId = null) {
            FieldId = fieldId;
            TypeTagValue = typeTagValue;
            TargetSchemaId = targetSchemaId;
        }

        public int FieldId { get; }

        public int TypeTagValue { get; }
        public string? TargetSchemaId { get; }
    }

    private readonly struct SnapshotHistoryModel {
        public SnapshotHistoryModel(
            string path,
            string schemaId,
            int version,
            List<SnapshotFieldModel> fields,
            SchemaReference? baseSchema = null) {
            Path = path;
            SchemaId = schemaId;
            Version = version;
            Fields = fields;
            BaseSchema = baseSchema;
        }

        public string Path { get; }

        public string SchemaId { get; }

        public int Version { get; }

        public List<SnapshotFieldModel> Fields { get; }

        public SchemaReference? BaseSchema { get; }
    }

    private readonly struct SnapshotVersionModel {
        public SnapshotVersionModel(
            int version,
            List<SnapshotFieldModel> fields) {
            Version = version;
            Fields = fields;
        }

        public int Version { get; }

        public List<SnapshotFieldModel> Fields { get; }
    }

    private readonly struct DurableSnapshotTypeModel {
        public DurableSnapshotTypeModel(
            DurableTypeModel currentType,
            List<SnapshotVersionModel> versions) {
            CurrentType = currentType;
            Versions = versions;
        }

        public DurableTypeModel CurrentType { get; }

        public List<SnapshotVersionModel> Versions { get; }
    }
}
