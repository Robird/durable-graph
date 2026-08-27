using System.Reflection;
using Atelia.DurableGraph;
using Atelia.DurableGraph.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Atelia.DurableGraph.Tests;

public sealed class DurableSchemaGeneratorTests {
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);

    [Fact]
    public void GeneratorProducesAnExecutableCanonicalSchema() {
        const string source = """
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType("samples.person", version: 3)]
            public sealed partial class Person : DurableBase {
                [DurableField(9)]
                private string _name = string.Empty;

                [Transient]
                private long _cachedScore;

                [DurableField(3)]
                private int _age;
            }
            """;

        GeneratorTestRun run = RunGenerator(
            source,
            SnapshotHistory("person-v1.dgsnapshot", "samples.person", 1, (3, 2), (9, 4)),
            SnapshotHistory("person-v2.dgsnapshot", "samples.person", 2, (3, 2), (9, 4)));

        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type? generatedType = assembly.GetType("Samples.Person");
        Assert.NotNull(generatedType);
        PropertyInfo? schemaProperty = generatedType.GetProperty(
            "Schema",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(schemaProperty);
        DurableSchema schema = Assert.IsType<DurableSchema>(schemaProperty.GetValue(null));

        Assert.Equal("samples.person", schema.SchemaId);
        Assert.Equal(3, schema.Version);
        Assert.Equal<DurableFieldInfo>(
            [
                new DurableFieldInfo(3, TypeTag.Int32),
                new DurableFieldInfo(9, TypeTag.String),
            ],
            schema.Fields);
    }

    [Fact]
    public void GeneratedSerializerRunsTheSchemaGatedStateStoreDemo() {
        const string source = """
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType("samples.person", version: 1)]
            public sealed partial class Person : DurableBase {
                [DurableField(1)] private bool _active;
                [DurableField(2)] private int _age;
                [DurableField(3)] private long _score;
                [DurableField(4)] private string _name;
                [Transient] private int _cachedRank = 123;

                public Person(bool active, int age, long score, string name) {
                    ConstructorCalls++;
                    _active = active;
                    _age = age;
                    _score = score;
                    _name = name;
                }

                public static int ConstructorCalls { get; private set; }
                public bool Active => _active;
                public int Age => _age;
                public long Score => _score;
                public string Name => _name;
                public int CachedRank => _cachedRank;
            }
            """;
        GeneratorTestRun run = RunGenerator(source);
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);
        string generatedSource = GeneratedSource(run, "DurableSchemas.g.cs");
        Assert.Contains("RuntimeHelpers.GetUninitializedObject", generatedSource);
        Assert.DoesNotContain("FormatterServices", generatedSource);

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        Type? personType = assembly.GetType("Samples.Person");
        Assert.NotNull(personType);
        object? sourceValue = Activator.CreateInstance(
            personType,
            new object?[] { true, 42, 900L, "Ada" });
        Assert.NotNull(sourceValue);
        PropertyInfo? serializerProperty = personType.GetProperty(
            "Serializer",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(serializerProperty);
        object? serializer = serializerProperty.GetValue(null);
        Assert.NotNull(serializer);
        InMemoryStateStore stateStore = new();

        InvokeStateStore(
            stateStore,
            nameof(InMemoryStateStore.Save),
            personType,
            "root",
            sourceValue,
            serializer);
        object? loaded = InvokeStateStore(
            stateStore,
            nameof(InMemoryStateStore.Load),
            personType,
            "root",
            serializer);

        Assert.NotNull(loaded);
        Assert.Equal(1, personType.GetProperty("ConstructorCalls")!.GetValue(null));
        Assert.Equal(true, personType.GetProperty("Active")!.GetValue(loaded));
        Assert.Equal(42, personType.GetProperty("Age")!.GetValue(loaded));
        Assert.Equal(900L, personType.GetProperty("Score")!.GetValue(loaded));
        Assert.Equal("Ada", personType.GetProperty("Name")!.GetValue(loaded));
        Assert.Equal(0, personType.GetProperty("CachedRank")!.GetValue(loaded));

        DurableSchema schema = Assert.IsType<DurableSchema>(
            personType.GetProperty("Schema")!.GetValue(null));
        Assert.Same(
            schema,
            stateStore.SchemaStore.GetRequired("samples.person", version: 1));
    }

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void InvalidDurableDeclarationsFailWithPreciseDiagnostic(
        string expectedDiagnosticId,
        string source) {
        GeneratorTestRun run = RunGenerator(source);

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == expectedDiagnosticId);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(run.GeneratedSources);
    }

    [Fact]
    public void GeneratedOutputIsIndependentOfTypeDeclarationOrder() {
        const string firstOrder = """
            using Atelia.DurableGraph;

            namespace Samples {
                [DurableType("samples.second", 1)]
                public sealed partial class Second : DurableBase {
                    [DurableField(1)] private long _value;
                }

                [DurableType("samples.first", 1)]
                public sealed partial class First : DurableBase {
                    [DurableField(1)] private bool _value;
                }
            }
            """;
        const string secondOrder = """
            using Atelia.DurableGraph;

            namespace Samples {
                [DurableType("samples.first", 1)]
                public sealed partial class First : DurableBase {
                    [DurableField(1)] private bool _value;
                }

                [DurableType("samples.second", 1)]
                public sealed partial class Second : DurableBase {
                    [DurableField(1)] private long _value;
                }
            }
            """;

        GeneratorTestRun first = RunGenerator(firstOrder);
        GeneratorTestRun second = RunGenerator(secondOrder);

        foreach (string hintName in new[] {
            "DurableSchemas.g.cs",
            "DurableGraphSnapshotCandidates.g.cs",
            "DurableSnapshots.g.cs",
        }) {
            string firstGenerated = GeneratedSource(first, hintName);
            string secondGenerated = GeneratedSource(second, hintName);
            Assert.Equal(firstGenerated, secondGenerated);
            Assert.DoesNotContain('\r', firstGenerated);
        }
    }

    [Fact]
    public void GeneratedOutputIsIndependentOfFieldDeclarationOrder() {
        string firstOrder = DurableTypeSource(
            "[DurableField(9)] private string _name = string.Empty;\n" +
            "[DurableField(3)] private int _age;");
        string secondOrder = DurableTypeSource(
            "[DurableField(3)] private int _age;\n" +
            "[DurableField(9)] private string _name = string.Empty;");

        GeneratorTestRun first = RunGenerator(firstOrder);
        GeneratorTestRun second = RunGenerator(secondOrder);

        foreach (string hintName in new[] {
            "DurableSchemas.g.cs",
            "DurableGraphSnapshotCandidates.g.cs",
            "DurableSnapshots.g.cs",
        }) {
            string firstGenerated = GeneratedSource(first, hintName);
            string secondGenerated = GeneratedSource(second, hintName);
            Assert.Equal(firstGenerated, secondGenerated);
        }
    }

    [Fact]
    public void VersionOneEmitsCanonicalCandidateAndCurrentSnapshot() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource(
                "[DurableField(9)] private string _name = string.Empty;\n" +
                "[DurableField(3)] private int _age;"),
            SnapshotHistory("unrelated.dgsnapshot", "samples.unrelated", 7, (1, 1)));

        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);
        Assert.Equal(
            "// durable-graph-snapshot-manifest:1\n" +
            "// snapshot-begin\n" +
            "// schema-id-base64:c2FtcGxlcy5leGFtcGxl\n" +
            "// version:1\n" +
            "// field:3|2\n" +
            "// field:9|4\n" +
            "// snapshot-end\n",
            GeneratedSource(run, "DurableGraphSnapshotCandidates.g.cs"));

        string snapshots = GeneratedSource(run, "DurableSnapshots.g.cs");
        Assert.Contains("private struct __DurableSnapshotV1", snapshots);
        Assert.Contains("public global::System.Int32 Field3;", snapshots);
        Assert.Contains("public global::System.String Field9;", snapshots);
    }

    [Fact]
    public void CompilationWithoutDurableTypesEmitsAnEmptyCandidateManifest() {
        GeneratorTestRun run = RunGenerator(
            "namespace Samples; internal sealed class PlainType { }");

        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.Equal(
            "// durable-graph-snapshot-manifest:1\n",
            GeneratedSource(run, "DurableGraphSnapshotCandidates.g.cs"));
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == "DurableSnapshots.g.cs");
    }

    [Fact]
    public void VersionTwoConsumesHistoryAsStronglyTypedSnapshots() {
        const string source = """
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType("samples.person", 2)]
            public sealed partial class Person : DurableBase {
                [DurableField(1)] private string _name = string.Empty;
                [DurableField(2)] private bool _active;

                private static void Upgrade(
                    in __DurableSnapshotV1 oldValue,
                    out __DurableSnapshotV2 newValue) {
                    newValue.Field1 = oldValue.Field1;
                    newValue.Field2 = true;
                }
            }
            """;
        GeneratorTestRun run = RunGenerator(
            source,
            SnapshotHistory("person-v1.dgsnapshot", "samples.person", 1, (1, 4)),
            SnapshotHistory("person-v2.dgsnapshot", "samples.person", 2, (1, 4), (2, 1)));

        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);

        string snapshots = GeneratedSource(run, "DurableSnapshots.g.cs");
        Assert.Contains("private struct __DurableSnapshotV1", snapshots);
        Assert.Contains("private struct __DurableSnapshotV2", snapshots);
        Assert.Contains("public global::System.String Field1;", snapshots);
        Assert.Contains("public global::System.Boolean Field2;", snapshots);
    }

    [Fact]
    public void MissingPredecessorHistoryFailsClosed() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource(
                "[DurableField(1)] private int _value;",
                durableTypeArguments: "\"samples.example\", 3"),
            SnapshotHistory("example-v1.dgsnapshot", "samples.example", 1, (1, 2)));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0014");
        Assert.Contains("version 2", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == "DurableSnapshots.g.cs");
    }

    [Fact]
    public void MalformedSnapshotHistoryFailsClosed() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource("[DurableField(1)] private int _value;"),
            new InMemoryAdditionalText(
                "broken.dgsnapshot",
                "// durable-graph-snapshot:1\n// not-a-snapshot\n"));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0012");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("broken.dgsnapshot", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void CandidateManifestCannotMasqueradeAsAcceptedHistory() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource("[DurableField(1)] private int _value;"),
            new InMemoryAdditionalText(
                "candidate.dgsnapshot",
                "// durable-graph-snapshot-manifest:1\n" +
                "// snapshot-begin\n" +
                "// schema-id-base64:c2FtcGxlcy5leGFtcGxl\n" +
                "// version:1\n" +
                "// field:1|2\n" +
                "// snapshot-end\n"));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0012");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void ConflictingHistoryForSameSchemaVersionFailsClosed() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource(
                "[DurableField(1)] private int _value;",
                durableTypeArguments: "\"samples.example\", 2"),
            SnapshotHistory("first.dgsnapshot", "samples.example", 1, (1, 2)),
            SnapshotHistory("second.dgsnapshot", "samples.example", 1, (1, 3)));

        Assert.Contains(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0013");
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == "DurableSnapshots.g.cs");
    }

    [Fact]
    public void CurrentHistoryMustExactlyMatchCurrentFields() {
        GeneratorTestRun run = RunGenerator(
            DurableTypeSource("[DurableField(1)] private int _value;"),
            SnapshotHistory("current.dgsnapshot", "samples.example", 1, (1, 3)));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0015");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == "DurableSnapshots.g.cs");
    }

    [Fact]
    public void ExistingGeneratedSnapshotNameFailsClosed() {
        GeneratorTestRun run = RunGenerator(DurableTypeSource(
            "[DurableField(1)] private int _value;\n" +
            "private struct __DurableSnapshotV1 { }"));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0016");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == "DurableSnapshots.g.cs");
    }

    public static TheoryData<string, string> InvalidSources() {
        return new TheoryData<string, string> {
            {
                "DG0001",
                DurableTypeSource(
                    "[Transient] private int _value;",
                    typeDeclaration: "public sealed class Example : DurableBase")
            },
            {
                "DG0001",
                DurableTypeSource(
                    "[Transient] private int _value;",
                    typeDeclaration: "public abstract partial class Example : DurableBase")
            },
            {
                "DG0001",
                DurableTypeSource(
                    "[Transient] private int _value;",
                    typeDeclaration: "public partial class Example : DurableBase")
            },
            {
                "DG0001",
                """
                using Atelia.DurableGraph;

                namespace Atelia.DurableGraph {
                    public static class Lookalikes {
                        public abstract class DurableBase { }
                    }
                }

                namespace Samples {
                    [DurableType("samples.example", 1)]
                    public sealed partial class Example
                        : Atelia.DurableGraph.Lookalikes.DurableBase {
                        [Transient] private int _value;
                    }
                }
                """
            },
            {
                "DG0002",
                DurableTypeSource(
                    "[Transient] private int _value;",
                    durableTypeArguments: "\"   \", 1")
            },
            {
                "DG0002",
                DurableTypeSource(
                    "[Transient] private int _value;",
                    durableTypeArguments: "\"\\uD800\", 1")
            },
            {
                "DG0003",
                DurableTypeSource("private int _value;")
            },
            {
                "DG0003",
                """
                using System;
                using Atelia.DurableGraph;

                namespace Atelia.DurableGraph {
                    public static class Lookalikes {
                        [AttributeUsage(AttributeTargets.Field)]
                        public sealed class DurableFieldAttribute : Attribute {
                            public DurableFieldAttribute(int id) { }
                        }
                    }
                }

                namespace Samples {
                    [DurableType("samples.example", 1)]
                    public sealed partial class Example : DurableBase {
                        [Atelia.DurableGraph.Lookalikes.DurableField(1)]
                        private int _value;
                    }
                }
                """
            },
            {
                "DG0004",
                DurableTypeSource("[DurableField(1), Transient] private int _value;")
            },
            {
                "DG0005",
                DurableTypeSource("[DurableField(0)] private int _value;")
            },
            {
                "DG0006",
                DurableTypeSource(
                    "[DurableField(1)] private int _first;\n" +
                    "[DurableField(1)] private string _second = string.Empty;")
            },
            {
                "DG0007",
                DurableTypeSource("[DurableField(1)] private double _value;")
            },
            {
                "DG0008",
                DurableTypeSource(
                    "[Transient] private int _value;\n" +
                    "public static DurableSchema Schema => throw null!;")
            },
            {
                "DG0008",
                """
                using Atelia.DurableGraph;

                namespace Samples;

                [DurableType("samples.schema", 1)]
                public sealed partial class Schema : DurableBase {
                    [Transient] private int _value;
                }
                """
            },
            {
                "DG0009",
                DurableTypeSource("[DurableField(1)] private static int _value;")
            },
            {
                "DG0010",
                DurableTypeSource(
                    "[Transient] private int _value;\n" +
                    "public static object Serializer => new object();")
            },
            {
                "DG0011",
                DurableTypeSource("[DurableField(1)] private readonly int _value;")
            },
        };
    }

    private static string DurableTypeSource(
        string members,
        string durableTypeArguments = "\"samples.example\", 1",
        string typeDeclaration = "public sealed partial class Example : DurableBase") {
        return $$"""
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType({{durableTypeArguments}})]
            {{typeDeclaration}} {
                {{members}}
            }
            """;
    }

    private static GeneratorTestRun RunGenerator(
        string source,
        params AdditionalText[] additionalTexts) {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"GeneratorTests_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: PlatformReferences().Append(
                MetadataReference.CreateFromFile(typeof(DurableBase).Assembly.Location)),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new DurableSchemaGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out _);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        return new GeneratorTestRun(
            (CSharpCompilation)outputCompilation,
            runResult.Diagnostics,
            runResult.Results.SelectMany(result => result.GeneratedSources).ToArray());
    }

    private static string GeneratedSource(
        GeneratorTestRun run,
        string hintName) {
        return Assert.Single(
            run.GeneratedSources,
            source => source.HintName == hintName).SourceText.ToString();
    }

    private static AdditionalText SnapshotHistory(
        string path,
        string schemaId,
        int version,
        params (int FieldId, int TypeTag)[] fields) {
        string content =
            "// durable-graph-snapshot:1\n" +
            "// snapshot-begin\n" +
            "// schema-id-base64:" +
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(schemaId)) +
            "\n// version:" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\n";

        foreach ((int fieldId, int typeTag) in fields) {
            content += "// field:" +
                fieldId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "|" +
                typeTag.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\n";
        }

        content += "// snapshot-end\n";
        return new InMemoryAdditionalText(path, content);
    }

    private static IEnumerable<MetadataReference> PlatformReferences() {
        string trustedAssemblies = Assert.IsType<string>(
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));

        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static Assembly EmitAndLoad(CSharpCompilation compilation) {
        using MemoryStream assemblyStream = new();
        EmitResult result = compilation.Emit(assemblyStream);

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));

        return Assembly.Load(assemblyStream.ToArray());
    }

    private static object? InvokeStateStore(
        InMemoryStateStore stateStore,
        string methodName,
        Type durableType,
        params object?[] arguments) {
        MethodInfo method = Assert.Single(
            typeof(InMemoryStateStore).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == methodName);

        return method.MakeGenericMethod(durableType).Invoke(stateStore, arguments);
    }

    private static bool IsError(Diagnostic diagnostic) {
        return diagnostic.Severity == DiagnosticSeverity.Error;
    }

    private sealed record GeneratorTestRun(
        CSharpCompilation OutputCompilation,
        IEnumerable<Diagnostic> GeneratorDiagnostics,
        GeneratedSourceResult[] GeneratedSources);

    private sealed class InMemoryAdditionalText : AdditionalText {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content) {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }

        public override SourceText GetText(
            CancellationToken cancellationToken = default) {
            return _text;
        }
    }
}
