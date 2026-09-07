using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
var shared = File.ReadAllText(Path.Combine(fixtures, "Shared.cs.txt"));
var first = Compile("CurrentShape", shared, File.ReadAllText(Path.Combine(fixtures, "Current.cs.txt")));
Run(first, "CurrentChecks");
var body = first.GetType("BoxProjection`3")!.GetMethod("Capture")!;
var codes = ReadOpcodes(body.GetMethodBody()!.GetILAsByteArray()!);
Require(codes.Contains(OpCodes.Constrained.Value) && codes.Contains(OpCodes.Call.Value)
    && !codes.Contains(OpCodes.Callvirt.Value), "Static helper IL must use constrained call, without instance callvirt.");
Console.WriteLine("StaticHelperConstrainedCallWithoutCallvirt:True");

var namedHistory = File.ReadAllText(Path.Combine(fixtures, "NamedHistory.cs.txt"));
var second = Compile("HistoricalShape", shared, File.ReadAllText(Path.Combine(fixtures, "Historical.cs.txt")), namedHistory);
Require(second.GetType("Point") is null && second.GetType("Box`1") is null,
    "Historical assembly must contain neither old domain Point nor Box<T>.");
Require(second.GetReferencedAssemblies().All(reference => reference.Name != first.GetName().Name),
    "Historical assembly must not reference the current-domain assembly.");
var historicalBytes = (byte[])first.GetType("CurrentChecks")!.GetMethod("HistoricalPayload")!.Invoke(null, null)!;
Run(second, "HistoricalChecks", historicalBytes);
Run(second, "NamedHistoryChecks");
Console.WriteLine("HistoricalAssemblyWithoutOldDomainTypes:True");

var unsupported = CompileDiagnostics("""
public static class MissingBusinessConversion {
    public static BoxStateV2<TNew> Upgrade<TOld, TNew>(BoxStateV1<TOld> old)
        where TOld : unmanaged where TNew : unmanaged => new(old.Value, 0);
}
""");
Require(unsupported.Any(diagnostic => diagnostic.Id == "CS1503"),
    "Unrelated old/new state parameters must not compile as an implicit business conversion.");
Console.WriteLine("MissingBusinessConversionRejected:CS1503");

var accessorConstraints = CompileDiagnostics("""
public sealed class Domain<T> where T : class { }
public static class InvalidAccess<T> { public static void Read(Domain<T> value) { } }
""");
Require(accessorConstraints.Any(diagnostic => diagnostic.Id == "CS0452"),
    "Domain constraints cannot be omitted from an accessor template.");
Console.WriteLine("MissingDomainConstraintRejected:CS0452");

var uninferredPhantom = NamedDiagnostics("""
public static class UninferredPhantom {
    public static void Attempt() {
        var old = new Generated.Family_Phantom.V1(12);
        Generated.Family_Phantom.V2<Generated.Family_Point.V1> middle = PhantomOwnerEdges.AddField(in old);
    }
}
""");
Require(uninferredPhantom.Any(diagnostic => diagnostic.Id == "CS0411"),
    "A phantom source has no input from which C# can infer the new representation parameter.");
Console.WriteLine("PhantomFirstEdgeCannotInferStateParameter:CS0411");

var wrongMiddle = NamedDiagnostics("""
public static class CurrentRepresentationAsHistoricalMiddle {
    public static void Attempt() {
        var wrong = new Generated.Family_Phantom.V2<Generated.Family_Point.V2>(new(12, 7));
        PhantomOwnerEdges.PointV1ToV2(in wrong);
    }
}
""");
Require(wrongMiddle.Any(diagnostic => diagnostic.Id == "CS1503"),
    "Today's Point V2 representation cannot stand in for the historical Point V1 intermediate.");
Console.WriteLine("CurrentStateCannotReplaceHistoricalMiddle:CS1503");
Console.WriteLine("GenericBindingShapeProbe:PASS");

Assembly Compile(string name, params string[] sources) {
    var compilation = MakeCompilation(name, sources);
    using var stream = new MemoryStream();
    var result = compilation.Emit(stream);
    if (!result.Success) {
        throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    }
    stream.Position = 0;
    return AssemblyLoadContext.Default.LoadFromStream(stream);
}

CSharpCompilation MakeCompilation(string name, params string[] sources) => CSharpCompilation.Create(name,
    sources.Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))),
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));

IEnumerable<Diagnostic> CompileDiagnostics(string source) =>
    MakeCompilation("RejectedShape", shared, source).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

IEnumerable<Diagnostic> NamedDiagnostics(string source) =>
    MakeCompilation("RejectedNamedShape", shared, namedHistory, source).GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error);

static void Run(Assembly assembly, string type, params object[] arguments) {
    try {
        foreach (var result in (string[])assembly.GetType(type)!.GetMethod("Run")!.Invoke(null, arguments)!) {
            Console.WriteLine(result);
        }
    }
    catch (TargetInvocationException error) when (error.InnerException is not null) {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        throw;
    }
}

static HashSet<short> ReadOpcodes(byte[] bytes) {
    var known = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(op => op.Value);
    var found = new HashSet<short>();
    for (var cursor = 0; cursor < bytes.Length;) {
        var code = bytes[cursor++];
        short value = code == 0xfe ? unchecked((short)(0xfe00 | bytes[cursor++])) : code;
        var op = known[value];
        found.Add(value);
        cursor += op.OperandType switch {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, cursor),
            _ => 4
        };
    }
    return found;
}

static void Require(bool condition, string message) {
    if (!condition) {
        throw new InvalidOperationException(message);
    }
}
