using System.Reflection;
using Atelia.DurableGraph;
using Atelia.DurableGraph.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

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

        GeneratorTestRun run = RunGenerator(source);

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
        string generatedSource = Assert.Single(run.GeneratedSources).SourceText.ToString();
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

        string firstGenerated = Assert.Single(first.GeneratedSources).SourceText.ToString();
        string secondGenerated = Assert.Single(second.GeneratedSources).SourceText.ToString();
        Assert.Equal(firstGenerated, secondGenerated);
        Assert.DoesNotContain('\r', firstGenerated);
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

        string firstGenerated = Assert.Single(first.GeneratedSources).SourceText.ToString();
        string secondGenerated = Assert.Single(second.GeneratedSources).SourceText.ToString();
        Assert.Equal(firstGenerated, secondGenerated);
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

    private static GeneratorTestRun RunGenerator(string source) {
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
}
