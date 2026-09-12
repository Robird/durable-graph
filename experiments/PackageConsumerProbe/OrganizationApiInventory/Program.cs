using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Each invocation loads exactly one explicit DLL closure in a fresh process.
// Compare normalized artifacts, never load old and new assemblies together.
if (args.Length == 3 && args[0] == "compare") {
    var before = File.ReadAllLines(args[1]);
    var after = File.ReadAllLines(args[2]);
    var removed = before.Except(after, StringComparer.Ordinal).ToArray();
    var added = after.Except(before, StringComparer.Ordinal).ToArray();
    foreach (var line in removed) Console.WriteLine("- " + line);
    foreach (var line in added) Console.WriteLine("+ " + line);
    Console.WriteLine($"Inventory comparison: removed={removed.Length}, added={added.Length}, oldLines={before.Length}, newLines={after.Length}");
    return removed.Length == 0 && added.Length == 0 && before.Length == after.Length ? 0 : 1;
}
if (args.Length != 3 || args[0] != "capture") {
    Console.Error.WriteLine("Usage: capture <directory-containing-explicit-runtime-DLL-closure> <output-directory> | compare <old/normalized.txt> <new/normalized.txt>");
    return 2;
}

var input = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var loader = new AssemblyLoadContext("ExplicitInventoryClosure", isCollectible: true);
loader.Resolving += (_, name) => {
    var candidate = Path.Combine(input, name.Name + ".dll");
    if (File.Exists(candidate)) return loader.LoadFromAssemblyPath(candidate);
    if (name.Name?.StartsWith("Atelia.", StringComparison.Ordinal) == true)
        throw new FileNotFoundException("Explicit inventory closure missing dependency", candidate);
    return null;
};
string[] oldNames = ["Atelia.DurableGraph", "Atelia.DurableGraph.StateStore.Serialization", "Atelia.DurableGraph.StateStore.Storage", "Atelia.DurableGraph.StateStore"];
string[] newNames = ["Atelia.DurableGraph", "Atelia.DurableGraph.Serialization", "Atelia.DurableGraph.Storage", "Atelia.DurableGraph.Persistence"];
var legacy = File.Exists(Path.Combine(input, oldNames[1] + ".dll"));
var names = legacy ? oldNames : newNames;
if ((legacy ? newNames : oldNames).Skip(1).Any(n => File.Exists(Path.Combine(input, n + ".dll"))))
    throw new InvalidOperationException("Mixed old/new DLL closure is forbidden.");
var assemblies = names.Select(n => loader.LoadFromAssemblyPath(Path.Combine(input, n + ".dll"))).ToArray();
var types = assemblies.SelectMany(a => a.GetTypes()).ToArray();
var roots = new HashSet<string>("IDurableObject DurableTypeAttribute DurableFieldAttribute TransientAttribute DurableSchemaExportAttribute DurableUpgradeAttribute ValueUpgradeRuleSetAttribute DurableValueUpgradeAttribute UpgradeDependencyAttribute ObjectId UpgradeContext ValueUpgrade`2 IStateModelRegistration IStateReaderRegistration IStateDefinitionRegistration ListDeltaAlgorithm DictionaryComparerKind DurableUpgradeException SchemaConflictException SchemaNotFoundException UnsupportedSchemaVersionException".Split(' '), StringComparer.Ordinal);
var schemas = new HashSet<string>("DurableSchema DurableFieldInfo TypeExpr TypeExprKind TypeTag TypeTagFacts SchemaKind ObjectLayout ArrayLayout ListLayout DictionaryLayout NullableValueLayout ObjectStateKind".Split(' '), StringComparer.Ordinal);
string AssemblyName(string name) => !legacy ? name : name switch {
    "Atelia.DurableGraph.StateStore.Serialization" => newNames[1],
    "Atelia.DurableGraph.StateStore.Storage" => newNames[2],
    "Atelia.DurableGraph.StateStore" => newNames[3],
    _ => name
};
string DefinitionName(Type type) {
    if (type.IsNested) return DefinitionName(type.DeclaringType!) + "+" + type.Name;
    var ns = type.Namespace ?? "";
    if (legacy && type.Assembly.GetName().Name == oldNames[0] && ns == oldNames[0]) {
        if (schemas.Contains(type.Name)) ns += ".Schema";
        else if (!roots.Contains(type.Name)) ns += ".Runtime";
    }
    else ns = AssemblyName(ns);
    return ns.Length == 0 ? type.Name : ns + "." + type.Name;
}
string TypeName(Type type) {
    if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
    if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
    if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
    if (type.IsArray) return TypeName(type.GetElementType()!) + (type.IsSZArray ? "[]" : "[" + new string(',', type.GetArrayRank() - 1) + "*]");
    if (type.IsFunctionPointer) return "fn(" + string.Join(",", type.GetFunctionPointerCallingConventions().Select(TypeName)) + ")(" + string.Join(",", type.GetFunctionPointerParameterTypes().Select(TypeName)) + ")->" + TypeName(type.GetFunctionPointerReturnType());
    var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    var result = "[" + AssemblyName(definition.Assembly.GetName().Name!) + "]" + DefinitionName(definition);
    return type.IsGenericType ? result + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : result;
}
var replacements = types.Where(t => t.FullName != null).Select(t => (Old: t.FullName!, New: DefinitionName(t)))
    .SelectMany(p => new[] { p, (Old: System.Text.RegularExpressions.Regex.Replace(p.Old, "`[0-9]+", ""), New: System.Text.RegularExpressions.Regex.Replace(p.New, "`[0-9]+", "")) })
    .Where(p => p.Old != p.New).Distinct().OrderByDescending(p => p.Old.Length).ToArray();
string MemberName(string name) {
    foreach (var pair in replacements) name = name.Replace(pair.Old, pair.New, StringComparison.Ordinal);
    if (legacy) foreach (var pair in oldNames.Zip(newNames).OrderByDescending(p => p.First.Length)) name = name.Replace(pair.First + ".", pair.Second + ".", StringComparison.Ordinal);
    return name;
}
string Value(object? value) => value switch {
    null => "null", string s => JsonSerializer.Serialize(s), char c => ((int)c).ToString(CultureInfo.InvariantCulture),
    Type t => TypeName(t), float f => "float:" + BitConverter.SingleToInt32Bits(f).ToString("X8"),
    double d => "double:" + BitConverter.DoubleToInt64Bits(d).ToString("X16"),
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? "null"
};
string Modifiers(Type[] required, Type[] optional) => " req[" + string.Join(",", required.Select(TypeName)) + "] opt[" + string.Join(",", optional.Select(TypeName)) + "]";
string AttributeArgument(CustomAttributeTypedArgument argument) => TypeName(argument.ArgumentType) + ":" + (argument.Value is IEnumerable<CustomAttributeTypedArgument> items ? "[" + string.Join(",", items.Select(AttributeArgument)) + "]" : Value(argument.Value));
string Attributes(IEnumerable<CustomAttributeData> attributes) => " attrs[" + string.Join(";", attributes.Select(a => TypeName(a.AttributeType) + "(" + string.Join(",", a.ConstructorArguments.Select(AttributeArgument)) + "){" + string.Join(",", a.NamedArguments.Select(n => n.MemberName + "=" + AttributeArgument(n.TypedValue)).Order(StringComparer.Ordinal)) + "}").Order(StringComparer.Ordinal)) + "]";
string Parameter(ParameterInfo p) => $"{p.Position}:{p.Name}:{TypeName(p.ParameterType)} flags={(int)p.Attributes} default={(p.HasDefaultValue ? Value(p.RawDefaultValue) : "none")}" + Modifiers(p.GetRequiredCustomModifiers(), p.GetOptionalCustomModifiers()) + Attributes(p.CustomAttributes);
string Constraints(IEnumerable<Type> parameters) => string.Join(";", parameters.Select(p => $"{p.GenericParameterPosition}:{p.Name}:flags={(int)p.GenericParameterAttributes}:" + string.Join(",", p.GetGenericParameterConstraints().Select(TypeName).Order(StringComparer.Ordinal)) + Attributes(p.CustomAttributes)));
var lines = new List<string>();
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
foreach (var a in assemblies) {
    lines.Add("ASSEMBLY " + AssemblyName(a.GetName().Name!) + " refs=" + string.Join(",", a.GetReferencedAssemblies().Select(n => AssemblyName(n.Name!)).Order(StringComparer.Ordinal)));
}
foreach (var t in types) {
    var prefix = TypeName(t);
    lines.Add($"TYPE {prefix} flags={(int)t.Attributes} base={(t.BaseType == null ? "none" : TypeName(t.BaseType))} interfaces=" + string.Join(",", t.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal)) + " constraints=" + Constraints(t.IsGenericTypeDefinition ? t.GetGenericArguments() : []) + Attributes(t.CustomAttributes));
    foreach (var f in t.GetFields(flags)) lines.Add($"FIELD {prefix} {MemberName(f.Name)}:{TypeName(f.FieldType)} flags={(int)f.Attributes} constant={(f.IsLiteral ? Value(f.GetRawConstantValue()) : "none")}" + Modifiers(f.GetRequiredCustomModifiers(), f.GetOptionalCustomModifiers()) + Attributes(f.CustomAttributes));
    foreach (var m in t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags))) {
        var method = m as MethodInfo;
        lines.Add($"METHOD {prefix} {MemberName(m.Name)} flags={(int)m.Attributes} impl={(int)m.GetMethodImplementationFlags()} calling={(int)m.CallingConvention} generic=" + Constraints(m.IsGenericMethodDefinition ? m.GetGenericArguments() : []) + " (" + string.Join(";", m.GetParameters().Select(Parameter)) + ") returns=" + (method == null ? "constructor" : Parameter(method.ReturnParameter)) + Attributes(m.CustomAttributes));
    }
    foreach (var p in t.GetProperties(flags)) lines.Add($"PROPERTY {prefix} {MemberName(p.Name)}:{TypeName(p.PropertyType)} flags={(int)p.Attributes} index=(" + string.Join(";", p.GetIndexParameters().Select(Parameter)) + ") accessors=" + string.Join(",", p.GetAccessors(true).Select(m => MemberName(m.Name)).Order(StringComparer.Ordinal)) + Modifiers(p.GetRequiredCustomModifiers(), p.GetOptionalCustomModifiers()) + Attributes(p.CustomAttributes));
    foreach (var e in t.GetEvents(flags)) lines.Add($"EVENT {prefix} {MemberName(e.Name)}:{TypeName(e.EventHandlerType!)} flags={(int)e.Attributes} add={e.AddMethod?.Name} remove={e.RemoveMethod?.Name} raise={e.RaiseMethod?.Name}" + Attributes(e.CustomAttributes));
}
lines.Sort(StringComparer.Ordinal);
if (lines.Count != lines.Distinct(StringComparer.Ordinal).Count()) throw new InvalidOperationException("Duplicate inventory records.");
File.WriteAllLines(Path.Combine(output, "normalized.txt"), lines);
File.WriteAllLines(Path.Combine(output, "types.tsv"), new[] { "assembly\tmetadataName\tcanonicalName\tflags\tcompilerGenerated\tnested" }.Concat(types.OrderBy(t => t.Assembly.GetName().Name).ThenBy(t => t.FullName).Select(t => $"{t.Assembly.GetName().Name}\t{t.FullName}\t{DefinitionName(t)}\t{t.Attributes}\t{t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)}\t{t.IsNested}")));
var top = types.Where(t => t.Assembly == assemblies[0] && !t.IsNested && t.Namespace?.StartsWith("Atelia.DurableGraph", StringComparison.Ordinal) == true && !t.Name.StartsWith('<')).ToArray();
var counts = top.GroupBy(t => DefinitionName(t)[..DefinitionName(t).LastIndexOf('.')]).ToDictionary(g => g.Key, g => g.Count());
var manifest = new {
    InputDirectory = input, Legacy = legacy, CoreSourceTopLevelCount = top.Length, CoreGroups = counts,
    TotalTypesIncludingCompilerArtifacts = types.Length, InventoryLines = lines.Count,
    Assemblies = loader.Assemblies.OrderBy(a => a.GetName().Name).Select(a => new { Identity = a.FullName, Path = a.Location, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }).ToArray()
};
var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "manifest.json"), json);
Console.WriteLine(json);
if (top.Length != 146 || counts.GetValueOrDefault("Atelia.DurableGraph") != 21 || counts.GetValueOrDefault("Atelia.DurableGraph.Schema") != 13 || counts.GetValueOrDefault("Atelia.DurableGraph.Runtime") != 112)
    throw new InvalidOperationException("Compiled core inventory does not match the approved 21/13/112 mapping.");
return 0;
