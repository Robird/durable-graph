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
                if (!IsBinaryField(field.TypeTagValue)) {
                    ReportInvalidBinaryBody(context, type.Symbol,
                        "field '" + field.Symbol.Name + "' is not a supported scalar or string reference",
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
                        if (!IsBinaryField(field.TypeTagValue)) {
                            ReportInvalidBinaryBody(context, type.Symbol,
                                "version " + version.ToString(CultureInfo.InvariantCulture) +
                                " field '" + field.Name +
                                "' is not a supported scalar or string reference");
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

    private static bool IsBinaryField(int typeTagValue) =>
        typeTagValue >= 1 && typeTagValue <= 14;

    // Schema tags describe domain fields. The DTO stores a string's identity as a UInt32 slot.
    private static int GetBinarySlotTypeTag(int typeTagValue) => typeTagValue == 4 ? 9 : typeTagValue;

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
            AppendBinaryPrepareBase(source, version, bodyIndent);
            AppendBinaryDtoRead(source, version, bodyIndent);
            AppendBinaryPrepareDelta(source, version, bodyIndent);
            AppendBinaryApplyDelta(source, version, bodyIndent);
            AppendBinaryStringReferenceValidation(source, version, bodyIndent);
        }

        BinaryVersionModel current = versions[versions.Count - 1];
        AppendBinaryCapture(source, type, current, bodyIndent, hasDomainBase);
        if (!type.Symbol.IsAbstract) {
            AppendBinaryAddRoot(source, type, current, bodyIndent);
        }

        source.Append(member).AppendLine("}");
        source.Append(indent).AppendLine("}");
        if (hasNamespace) {
            source.AppendLine("}");
        }
    }

    private static void AppendBinaryCapture(
        StringBuilder source, DurableTypeModel type, BinaryVersionModel version, string indent, bool hasDomainBase) {
        bool needsContext = version.HasStringReferences;
        source.Append(indent).Append("internal static ").Append(version.Name).Append(" Capture(")
            .Append(type.Symbol.ToDisplayString(FullyQualifiedNameFormat)).Append(" value");
        if (needsContext) {
            source.Append(", global::Atelia.DurableGraph.CaptureContext context");
        }

        source.AppendLine(") {");
        source.Append(indent).AppendLine("    global::System.ArgumentNullException.ThrowIfNull(value);");
        if (needsContext) {
            source.Append(indent).AppendLine("    global::System.ArgumentNullException.ThrowIfNull(context);");
        }

        int inheritedCount = version.Fields.Count - type.Fields.Count;
        if (hasDomainBase) {
            source.Append(indent).Append("    var baseState = ")
                .Append(type.Symbol.BaseType!.ToDisplayString(FullyQualifiedNameFormat))
                .Append('.').Append(BinaryBodyTypeName).Append(".Capture(value");
            for (int index = 0; index < inheritedCount; index++) {
                if (version.Fields[index].TypeTagValue == 4) {
                    source.Append(", context");
                    break;
                }
            }

            source.AppendLine(");");
        }

        source.Append(indent).Append("    return new ").Append(version.Name).Append('(');
        for (int index = 0; index < version.Fields.Count; index++) {
            if (index > 0) {
                source.Append(", ");
            }

            if (index < inheritedCount) {
                source.Append("baseState.").Append(version.Fields[index].Name);
            } else {
                bool isString = version.Fields[index].TypeTagValue == 4;
                if (isString) {
                    source.Append("context.CaptureString(");
                }

                source.Append("value.").Append(EscapeIdentifier(type.Fields[index - inheritedCount].Symbol.Name));
                if (isString) {
                    source.Append(')');
                }
            }
        }

        source.AppendLine(");");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryAddRoot(
        StringBuilder source, DurableTypeModel type, BinaryVersionModel version, string indent) {
        string domainType = type.Symbol.ToDisplayString(FullyQualifiedNameFormat);
        source.Append(indent).Append("private static readonly global::Atelia.DurableGraph.CapturedStatePreparation<")
            .Append(version.Name).Append("> Preparation = new(").Append(version.Name)
            .AppendLine(".Schema, PrepareBase, PrepareDelta);");
        source.Append(indent).Append("internal static uint AddRoot(global::Atelia.DurableGraph.CaptureContext context, ")
            .Append(domainType).AppendLine("? value) {");
        source.Append(indent).AppendLine("    global::System.ArgumentNullException.ThrowIfNull(context);");
        source.Append(indent).Append("    return context.AddRoot<").Append(domainType).Append(", ")
            .Append(version.Name).Append(">(value, ").Append(version.Name)
            .Append(".Schema, static (source, shared) => Capture(source");
        if (version.HasStringReferences) {
            source.Append(", shared");
        }

        source.AppendLine("), Preparation);");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryDto(
        StringBuilder source, DurableTypeModel type, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal readonly struct ").Append(version.Name).AppendLine(" {");
        foreach (BinaryFieldModel field in version.Fields) {
            TryGetFieldTypeName(GetBinarySlotTypeTag(field.TypeTagValue), out string? fieldType);
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
                TryGetFieldTypeName(GetBinarySlotTypeTag(field.TypeTagValue), out string? fieldType);
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
            source.Append(indent).Append("    writer.Write").Append(GetTypeTagName(GetBinarySlotTypeTag(field.TypeTagValue)))
                .Append("(value.").Append(field.Name).AppendLine(");");
        }

        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryPrepareBase(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static ").Append(PayloadNamespace)
            .Append("PreparedBase PrepareBase(in ").Append(version.Name).AppendLine(" current) {");
        // TODO(DB-029): Measure temporary buffers and owned-copy costs after MVP before adding pooling.
        source.Append(indent).AppendLine("    var buffer = new global::System.Buffers.ArrayBufferWriter<byte>();");
        source.Append(indent).Append("    var writer = new ").Append(PayloadNamespace)
            .AppendLine("BinaryPayloadWriter(buffer);");
        source.Append(indent).AppendLine("    Write(ref writer, in current);");
        source.Append(indent).Append("    return new ").Append(PayloadNamespace)
            .AppendLine("PreparedBase(buffer.WrittenSpan);");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryDtoRead(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static ").Append(version.Name).Append(" Read").Append(version.Name)
            .Append("(ref ").Append(PayloadNamespace).AppendLine("BinaryPayloadReader reader) {");
        foreach (BinaryFieldModel field in version.Fields) {
            source.Append(indent).Append("    var ").Append(field.ParameterName).Append(" = reader.Read")
                .Append(GetTypeTagName(GetBinarySlotTypeTag(field.TypeTagValue))).AppendLine("();");
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

    private static void AppendBinaryPrepareDelta(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static ").Append(PayloadNamespace)
            .Append("PreparedDelta PrepareDelta(in ").Append(version.Name).Append(" prior, in ")
            .Append(version.Name).AppendLine(" current) {");
        if (version.Fields.Count == 0) {
            source.Append(indent).Append("    return new ").Append(PayloadNamespace)
                .AppendLine("PreparedDelta(false, global::System.ReadOnlySpan<byte>.Empty);");
            source.Append(indent).AppendLine("}");
            return;
        }

        source.Append(indent).AppendLine("    bool hasChanges = false;");
        int maskCount = version.Fields.Count / 8 + (version.Fields.Count % 8 == 0 ? 0 : 1);
        for (int maskIndex = 0; maskIndex < maskCount; maskIndex++) {
            source.Append(indent).Append("    byte mask").Append(maskIndex.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" = 0;");
        }

        // Compare every slot once. The resulting masks are the sole authority for
        // both HasChanges and which current values are subsequently written.
        for (int index = 0; index < version.Fields.Count; index++) {
            BinaryFieldModel field = version.Fields[index];
            source.Append(indent).Append("    if (!(");
            AppendBinarySlotEquality(source, field, "prior." + field.Name, "current." + field.Name);
            source.AppendLine(")) {");
            source.Append(indent).Append("        mask").Append((index / 8).ToString(CultureInfo.InvariantCulture))
                .Append(" |= ").Append((1 << (index % 8)).ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            source.Append(indent).AppendLine("    }");
        }

        source.Append(indent).AppendLine("    var buffer = new global::System.Buffers.ArrayBufferWriter<byte>();");
        source.Append(indent).Append("    var writer = new ").Append(PayloadNamespace)
            .AppendLine("BinaryPayloadWriter(buffer);");
        for (int maskIndex = 0; maskIndex < maskCount; maskIndex++) {
            string maskName = "mask" + maskIndex.ToString(CultureInfo.InvariantCulture);
            source.Append(indent).Append("    hasChanges |= ").Append(maskName).AppendLine(" != 0;");
            source.Append(indent).Append("    writer.WriteByte(").Append(maskName).AppendLine(");");
        }

        for (int index = 0; index < version.Fields.Count; index++) {
            BinaryFieldModel field = version.Fields[index];
            source.Append(indent).Append("    if (");
            AppendBinaryDeltaBitTest(source, index);
            source.AppendLine(") {");
            source.Append(indent).Append("        writer.Write")
                .Append(GetTypeTagName(GetBinarySlotTypeTag(field.TypeTagValue)))
                .Append("(current.").Append(field.Name).AppendLine(");");
            source.Append(indent).AppendLine("    }");
        }

        source.Append(indent).Append("    return new ").Append(PayloadNamespace)
            .AppendLine("PreparedDelta(hasChanges, buffer.WrittenSpan);");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendBinaryApplyDelta(StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static ").Append(version.Name).Append(" ApplyDelta")
            .Append(version.Name).Append("(ref ").Append(PayloadNamespace)
            .Append("BinaryPayloadReader reader, in ").Append(version.Name).AppendLine(" prior) {");
        int maskCount = version.Fields.Count / 8 + (version.Fields.Count % 8 == 0 ? 0 : 1);
        for (int maskIndex = 0; maskIndex < maskCount; maskIndex++) {
            source.Append(indent).Append("    byte mask").Append(maskIndex.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" = reader.ReadByte();");
        }

        int remainingBits = version.Fields.Count % 8;
        if (remainingBits != 0) {
            source.Append(indent).Append("    if ((mask").Append((maskCount - 1).ToString(CultureInfo.InvariantCulture))
                .Append(" & ").Append((255 ^ ((1 << remainingBits) - 1)).ToString(CultureInfo.InvariantCulture))
                .AppendLine(") != 0) {");
            source.Append(indent).AppendLine("        throw new global::System.IO.InvalidDataException(\"Delta bitmap contains bits outside the exact DTO layout.\");");
            source.Append(indent).AppendLine("    }");
        }

        foreach (BinaryFieldModel field in version.Fields) {
            source.Append(indent).Append("    var ").Append(field.ParameterName).Append(" = prior.")
                .Append(field.Name).AppendLine(";");
        }

        for (int index = 0; index < version.Fields.Count; index++) {
            BinaryFieldModel field = version.Fields[index];
            source.Append(indent).Append("    if (");
            AppendBinaryDeltaBitTest(source, index);
            source.AppendLine(") {");
            source.Append(indent).Append("        ").Append(field.ParameterName).Append(" = reader.Read")
                .Append(GetTypeTagName(GetBinarySlotTypeTag(field.TypeTagValue))).AppendLine("();");
            source.Append(indent).Append("        if (");
            AppendBinarySlotEquality(source, field, "prior." + field.Name, field.ParameterName);
            source.AppendLine(") {");
            source.Append(indent).Append("            throw new global::System.IO.InvalidDataException(\"Delta redundantly changes slot ")
                .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(" to its prior value.\");");
            source.Append(indent).AppendLine("        }");
            source.Append(indent).AppendLine("    }");
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

    private static void AppendBinaryDeltaBitTest(StringBuilder source, int index) {
        source.Append("(mask").Append((index / 8).ToString(CultureInfo.InvariantCulture))
            .Append(" & ").Append((1 << (index % 8)).ToString(CultureInfo.InvariantCulture)).Append(") != 0");
    }

    private static void AppendBinarySlotEquality(
        StringBuilder source, BinaryFieldModel field, string left, string right) {
        // Match Base encoding's preserved bits, including signed zero and NaN payloads.
        string? bitConversion = field.TypeTagValue switch {
            12 => "HalfToUInt16Bits",
            13 => "SingleToUInt32Bits",
            14 => "DoubleToUInt64Bits",
            _ => null,
        };
        if (bitConversion is not null) {
            source.Append("global::System.BitConverter.").Append(bitConversion).Append('(').Append(left)
                .Append(") == global::System.BitConverter.").Append(bitConversion).Append('(').Append(right).Append(')');
        } else {
            source.Append(left).Append(" == ").Append(right);
        }
    }

    private static void AppendBinaryStringReferenceValidation(
        StringBuilder source, BinaryVersionModel version, string indent) {
        source.Append(indent).Append("internal static void ValidateStringReferences(in ").Append(version.Name)
            .AppendLine(" state, global::Atelia.DurableGraph.StringReadTable table) {");
        source.Append(indent).AppendLine("    global::System.ArgumentNullException.ThrowIfNull(table);");
        foreach (BinaryFieldModel field in version.Fields) {
            if (field.TypeTagValue == 4) {
                source.Append(indent).Append("    table.ResolveString(state.").Append(field.Name).AppendLine(");");
            }
        }

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
        public bool HasStringReferences => Fields.Exists(entry => entry.TypeTagValue == 4);
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
