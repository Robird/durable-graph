using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Generator;

public sealed partial class DurableSchemaGenerator {
    private sealed class GenericDomainField {
        internal GenericDomainField(DurableTypeModel declaration, IFieldSymbol symbol, int index) {
            Declaration = declaration; Symbol = symbol; Index = index;
        }
        internal DurableTypeModel Declaration { get; }
        internal IFieldSymbol Symbol { get; }
        internal int Index { get; }
        internal string DomainType => Symbol.Type.ToDisplayString(GenericQualifiedNameFormat);
        internal string OwnerType => Symbol.ContainingType.ToDisplayString(GenericQualifiedNameFormat);
        internal string Suffix => FamilyName(Declaration.SchemaId) + "_" + Number(Declaration.Fields[Index].FieldId);
    }

    private static List<GenericDomainField> GenericDomainFields(INamedTypeSymbol type, List<DurableTypeModel> types) {
        List<GenericDomainField> result = new();
        if (type.TypeKind == TypeKind.Class && !HasMetadataName(type.BaseType, DurableBaseMetadataName)) result.AddRange(GenericDomainFields(type.BaseType!, types));
        DurableTypeModel declaration = types.Single(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, type.OriginalDefinition));
        for (int index = 0; index < declaration.Fields.Count; index++) {
            IFieldSymbol symbol = (IFieldSymbol)type.GetMembers(declaration.Fields[index].Symbol.Name).Single();
            result.Add(new GenericDomainField(declaration, symbol, index));
        }
        return result;
    }

    private static string CurrentExecutionParameters(GenericLayout layout) => GenericList(Enumerable.Range(0, layout.DynamicFields.Count)
        .SelectMany(index => new[] { "TState" + Number(index), "TOps" + Number(index), "TProjection" + Number(index) }));

    private static void AppendCurrentExecutionConstraints(StringBuilder output, GenericLayout layout, List<GenericDomainField> domainFields) {
        AppendGenericConstraints(output, layout, true);
        for (int index = 0; index < layout.DynamicFields.Count; index++) {
            int fieldIndex = layout.Fields.IndexOf(layout.DynamicFields[index]);
            output.Append(" where TProjection").Append(index).Append(": struct, ").Append(RuntimeName)
                .Append("IValueProjection<").Append(domainFields[fieldIndex].DomainType).Append(", TState").Append(index).Append('>');
        }
    }

    private static string CurrentDto(DurableTypeModel type, GenericLayout layout) => FamilyType(type.SchemaId) + "." + layout.Dto;
    private static string CurrentBody(DurableTypeModel type, GenericLayout layout) => FamilyType(type.SchemaId) + "." + layout.Body;

    private static void AppendGenericDomainProjection(StringBuilder output, DurableTypeModel type, GenericLayout layout, List<DurableTypeModel> types) {
        if (type.IsEnum) { AppendEnumDomainProjection(output, type, layout); return; }
        bool hasNamespace = !type.Symbol.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace) output.Append("namespace ").Append(type.Symbol.ContainingNamespace.ToDisplayString()).AppendLine(" {");
        string domain = type.Symbol.ToDisplayString(GenericQualifiedNameFormat);
        string dto = CurrentDto(type, layout);
        string parameters = CurrentExecutionParameters(layout);
        List<GenericDomainField> fields = GenericDomainFields(type.Symbol, types);
        output.Append(type.IsInline ? "partial struct " : "partial class ").Append(EscapeIdentifier(type.Symbol.Name))
            .Append(DomainParameters(type.Symbol)).Append(DomainConstraints(type.Symbol)).AppendLine(" {");
        AppendGenericFieldAccessors(output, type);
        AppendGenericCurrentFactory(output, type, layout, fields);
        output.Append("    private static ").Append(dto).Append(" __DurableCapture").Append(parameters).Append('(')
            .Append(type.IsInline ? "in " : string.Empty).Append(domain).Append(" value, ").Append(RuntimeName).Append("CaptureContext context, ")
            .Append(RuntimeName).Append("DurableSchema schema)");
        AppendCurrentExecutionConstraints(output, layout, fields);
        output.AppendLine(" {");
        if (!type.IsInline) output.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
        for (int index = 0; index < fields.Count; index++) {
            GenericField stateField = layout.Fields[index];
            GenericDomainField field = fields[index];
            output.Append("        ref readonly var field").Append(index).Append(" = ref ").Append(field.OwnerType).Append(".__DurableRead_").Append(field.Suffix)
                .Append('(').Append(type.IsInline ? "in " : string.Empty).AppendLine("value);");
            output.Append("        var ").Append(stateField.Argument).Append(" = ");
            if (stateField.DynamicIndex >= 0) output.Append("TProjection").Append(stateField.DynamicIndex).Append(".Capture(in field").Append(index)
                .Append(", context, ").Append(stateField.Slot()).Append(')');
            else if (stateField.Field.InlineSchema.HasValue) output.Append(CurrentInlineProjection(field.Symbol.Type)).Append(".Capture(in field").Append(index)
                .Append(", context, ").Append(stateField.Slot()).Append(')');
            else if (GenericFieldTag(stateField) == 4) output.Append("context.CaptureString(field").Append(index).Append(')');
            else if (GenericFieldTag(stateField) == 15) output.Append("context.CaptureObject(field").Append(index).Append(", ").Append(stateField.Slot()).Append(".TargetType!)");
            else output.Append("field").Append(index);
            output.AppendLine(";");
        }
        output.Append("        return new ").Append(dto).Append('(').Append(string.Join(", ", layout.Fields.Select(field => field.Argument))).AppendLine(");");
        output.AppendLine("    }");
        AppendGenericCurrentHydrate(output, type, layout, fields);
        if (type.IsInline) {
            output.Append("    public readonly struct __DurableProjection").Append(parameters).Append(" : ").Append(RuntimeName)
                .Append("IValueProjection<").Append(domain).Append(", ").Append(dto).Append('>');
            AppendCurrentExecutionConstraints(output, layout, fields);
            output.AppendLine(" {");
            output.Append("        public static ").Append(dto).Append(" Capture(in ").Append(domain).Append(" value, ").Append(RuntimeName)
                .Append("CaptureContext context, ").Append(RuntimeName).Append("DurableFieldInfo slot) => __DurableCapture").Append(parameters).AppendLine("(in value, context, slot.InlineSchema!);");
            output.Append("        public static void Hydrate(ref ").Append(domain).Append(" target, in ").Append(dto).Append(" state, ").Append(RuntimeName)
                .Append("ObjectReadTable objects, ").Append(RuntimeName).Append("DurableFieldInfo slot) => __DurableHydrate").Append(parameters)
                .AppendLine("(ref target, in state, objects, slot.InlineSchema!);");
            output.AppendLine("    }");
        }
        output.AppendLine("}");
        if (hasNamespace) output.AppendLine("}");
    }

    private static void AppendGenericFieldAccessors(StringBuilder output, DurableTypeModel type) {
        string domain = type.Symbol.ToDisplayString(GenericQualifiedNameFormat);
        for (int index = 0; index < type.Fields.Count; index++) {
            DurableFieldModel field = type.Fields[index];
            string fieldType = field.Symbol.Type.ToDisplayString(GenericQualifiedNameFormat.WithMiscellaneousOptions(
                GenericQualifiedNameFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
            string suffix = FamilyName(type.SchemaId) + "_" + Number(field.FieldId);
            output.Append("    internal static ref readonly ").Append(fieldType).Append(" __DurableRead_").Append(suffix).Append('(')
                .Append(type.IsInline ? "in " : string.Empty).Append(domain).Append(" value) => ref value.").Append(EscapeIdentifier(field.Symbol.Name)).AppendLine(";");
            if (field.Symbol.IsReadOnly) {
                output.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ")
                    .Append(Literal(field.Symbol.Name)).AppendLine(")]");
                output.Append("    private static extern ref ").Append(fieldType).Append(" __DurableReadonly_").Append(suffix).Append('(')
                    .Append(type.IsInline ? "ref " : string.Empty).Append(domain).AppendLine(" value);");
            }
            output.Append("    internal static void __DurableWrite_").Append(suffix).Append('(').Append(type.IsInline ? "ref " : string.Empty)
                .Append(domain).Append(" value, in ").Append(fieldType).Append(" field) => ");
            if (field.Symbol.IsReadOnly) output.Append("__DurableReadonly_").Append(suffix).Append('(').Append(type.IsInline ? "ref " : string.Empty).Append("value)");
            else output.Append("value.").Append(EscapeIdentifier(field.Symbol.Name));
            output.AppendLine(" = field;");
        }
    }

    private static void AppendGenericCurrentHydrate(StringBuilder output, DurableTypeModel type, GenericLayout layout, List<GenericDomainField> fields) {
        string domain = type.Symbol.ToDisplayString(GenericQualifiedNameFormat);
        string dto = CurrentDto(type, layout);
        output.Append("    private static void __DurableHydrate").Append(CurrentExecutionParameters(layout)).Append('(')
            .Append(type.IsInline ? "ref " : string.Empty).Append(domain).Append(" target, in ").Append(dto).Append(" state, ")
            .Append(RuntimeName).Append("ObjectReadTable objects, ").Append(RuntimeName).Append("DurableSchema schema)");
        AppendCurrentExecutionConstraints(output, layout, fields);
        output.AppendLine(" {");
        if (type.IsInline) output.Append("        ").Append(domain).AppendLine(" value = default;");
        else output.AppendLine("        var value = target;");
        for (int index = 0; index < fields.Count; index++) {
            GenericField stateField = layout.Fields[index];
            GenericDomainField field = fields[index];
            output.Append("        ").Append(field.DomainType).Append(" field").Append(index).Append(" = ");
            if (stateField.DynamicIndex >= 0 || stateField.Field.InlineSchema.HasValue) {
                output.AppendLine("default!;");
                output.Append("        ").Append(stateField.DynamicIndex >= 0 ? "TProjection" + Number(stateField.DynamicIndex) : CurrentInlineProjection(field.Symbol.Type))
                    .Append(".Hydrate(ref field").Append(index).Append(", in state.").Append(stateField.Name).Append(", objects, ").Append(stateField.Slot()).AppendLine(");");
            } else if (GenericFieldTag(stateField) == 4) output.Append("objects.ResolveString(state.").Append(stateField.Name).AppendLine(")!;");
            else if (GenericFieldTag(stateField) == 15) output.Append("objects.ResolveObject<").Append(field.DomainType).Append(">(state.").Append(stateField.Name).AppendLine(")!;");
            else output.Append("state.").Append(stateField.Name).AppendLine(";");
        }
        for (int index = 0; index < fields.Count; index++) output.Append("        ").Append(fields[index].OwnerType).Append(".__DurableWrite_").Append(fields[index].Suffix)
            .Append('(').Append(type.IsInline ? "ref " : string.Empty).Append("value, in field").Append(index).AppendLine(");");
        if (type.IsInline) output.AppendLine("        target = value;");
        output.AppendLine("    }");
    }

    private static void AppendGenericCurrentFactory(StringBuilder output, DurableTypeModel type, GenericLayout layout, List<GenericDomainField> fields) {
        string domain = type.Symbol.ToDisplayString(GenericQualifiedNameFormat);
        string dto = CurrentDto(type, layout);
        string body = CurrentBody(type, layout);
        string parameters = CurrentExecutionParameters(layout);
        string result = RuntimeName + (type.IsInline ? "StateValueBinding" : "StateModelBinding");
        output.Append("    private static ").Append(result).Append(" __DurableCreateCurrent(").Append(RuntimeName).AppendLine("StateBindingContext context) {");
        output.Append("        var schema = new ").Append(RuntimeName).Append("DurableSchema(context.GetTypeExpr(typeof(").Append(domain).Append(")), ")
            .Append(type.Version).Append(", new ").Append(RuntimeName).AppendLine("DurableFieldInfo[] {");
        foreach (DurableFieldModel field in type.Fields) output.Append("            context.ResolveCurrentValue(typeof(").Append(field.Symbol.Type.ToDisplayString(GenericQualifiedNameFormat))
            .Append(")).WithFieldId(").Append(field.FieldId).AppendLine(").Slot,");
        output.Append("        }, ");
        if (!type.IsInline && !HasMetadataName(type.Symbol.BaseType, DurableBaseMetadataName)) output.Append("context.ResolveCurrentModel(typeof(")
            .Append(type.Symbol.BaseType!.ToDisplayString(GenericQualifiedNameFormat)).Append(")).CurrentSchema");
        else output.Append("null");
        output.Append(", ").Append(RuntimeName).Append("SchemaKind.").Append(type.IsInline ? "InlineValue" : "ReferenceObject").AppendLine(");");
        output.AppendLine("        context.BindSchema(schema);");
        for (int index = 0; index < layout.DynamicFields.Count; index++) output.Append("        var value").Append(index)
            .Append(" = context.ResolveCurrentValue(typeof(").Append(fields[layout.Fields.IndexOf(layout.DynamicFields[index])].DomainType).AppendLine("));");
        if (layout.DynamicFields.Count == 0) output.AppendLine("        return __DurableCreateTyped(schema, context);");
        else {
            output.Append("        var method = typeof(").Append(domain).Append(").GetMethod(\"__DurableCreateTyped\", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly)!")
                .Append(".MakeGenericMethod(").Append(string.Join(", ", Enumerable.Range(0, layout.DynamicFields.Count).SelectMany(index => new[] { "value" + Number(index) + ".StateType", "value" + Number(index) + ".StateOpsType", "value" + Number(index) + ".ProjectionType!" }))).AppendLine(");");
            output.Append("        return method.CreateDelegate<global::System.Func<").Append(RuntimeName).Append("DurableSchema, ").Append(RuntimeName)
                .Append("StateBindingContext, ").Append(result).AppendLine(">>()(schema, context);");
        }
        output.AppendLine("    }");
        output.Append("    private static ").Append(result).Append(" __DurableCreateTyped").Append(parameters).Append('(')
            .Append(RuntimeName).Append("DurableSchema schema, ").Append(RuntimeName).Append("StateBindingContext context)");
        AppendCurrentExecutionConstraints(output, layout, fields);
        output.AppendLine(" {");
        if (type.IsInline) {
            output.Append("        return new ").Append(RuntimeName).Append("StateValueBinding(new ").Append(RuntimeName).Append("DurableFieldInfo(1, ")
                .Append(RuntimeName).Append("TypeTag.InlineValue, inlineSchema: schema), typeof(").Append(dto).Append("), typeof(").Append(body)
                .Append("), typeof(").Append(domain).Append("), typeof(__DurableProjection").Append(parameters).AppendLine("));");
        } else {
            output.Append("        var preparation = new ").Append(RuntimeName).Append("CapturedStatePreparation<").Append(dto).AppendLine(">(schema,");
            output.Append("            (in ").Append(dto).Append(" state) => ").Append(body).AppendLine(".PrepareBase(in state, schema),");
            output.Append("            (in ").Append(dto).Append(" prior, in ").Append(dto).Append(" current) => ").Append(body).AppendLine(".PrepareDelta(in prior, in current, schema));");
            output.Append("        return new ").Append(RuntimeName).Append("StateModelBinding<").Append(domain).Append(", ").Append(dto).AppendLine(">(preparation,");
            output.Append("            new ").Append(RuntimeName).AppendLine("StateReaderBinding[] { context.ResolveReader(schema) },");
            output.Append("            item => context.Normalize<").Append(dto).AppendLine(">(item, schema),");
            if (type.Symbol.IsAbstract) output.AppendLine("            static () => throw new global::System.InvalidOperationException(\"An abstract durable model cannot be allocated.\"),");
            else output.Append("            static () => (").Append(domain).Append(")global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(").Append(domain).AppendLine(")),");
            output.Append("            (").Append(domain).Append(" value, in ").Append(dto).Append(" state, ").Append(RuntimeName).Append("ObjectReadTable objects) => __DurableHydrate")
                .Append(parameters).AppendLine("(value, in state, objects, schema),");
            output.Append("            (value, capture) => __DurableCapture").Append(parameters).AppendLine("(value, capture, schema),");
            output.Append("            (in ").Append(dto).Append(" state, ").Append(RuntimeName).Append("IStateReferenceVisitor visitor) => ").Append(body).AppendLine(".Visit(in state, visitor, schema),");
            output.AppendLine("            sourceReaderResolver: context.ResolveReader);");
        }
        output.AppendLine("    }");
    }
}
