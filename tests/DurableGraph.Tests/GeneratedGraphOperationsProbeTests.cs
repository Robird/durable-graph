using System.Linq.Expressions;
using System.Reflection;
using Atelia.DurableGraph;
using Atelia.DurableGraph.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    private const string GraphOperationsHintName =
        "DurableGraphOperationsProbe.g.cs";

    [Fact]
    public void ProbeCaptureAndReferenceVisitMatchTheIndependentOracle() {
        GeneratorTestRun run = RunGraphProbeGenerator(GraphNodeSource(reorderFields: false));
        AssertSuccessfulProbeRun(run);
        AssertTypedGeneratedContract(run.OutputCompilation);

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        GeneratedGraphHarness harness = new(assembly.GetRequiredType("Samples.Node"));
        object root = harness.CreateNode(1, true, 10, 100, "root");
        object child = harness.CreateNode(2, false, 20, 200, "child");
        harness.SetNext(root, child);
        harness.SetAlias(root, child);
        harness.SetNext(child, root);

        GeneratedCapture rootCapture = harness.Capture(root);
        GeneratedCapture childCapture = harness.Capture(child);

        AssertGeneratedSnapshot(
            harness,
            rootCapture.Snapshot,
            active: true,
            value: 10,
            score: 100,
            name: "root",
            nextId: 2,
            aliasId: 2);
        AssertGeneratedSnapshot(
            harness,
            childCapture.Snapshot,
            active: false,
            value: 20,
            score: 200,
            name: "child",
            nextId: 1,
            aliasId: null);

        object[] rootReferences = harness.Visit(rootCapture.References);
        Assert.Equal(2, rootReferences.Length);
        Assert.Same(child, rootReferences[0]);
        Assert.Same(child, rootReferences[1]);

        object childReference = Assert.Single(harness.Visit(childCapture.References));
        Assert.Same(root, childReference);

        ProbeNode oracleRoot = new(new ProbeId(1), 10);
        ProbeNode oracleChild = new(new ProbeId(2), 20);
        oracleRoot.Next = oracleChild;
        oracleRoot.Alias = oracleChild;
        oracleChild.Next = oracleRoot;
        NormalizedBaselineGraph oracle =
            GraphDeltaProbe.CaptureCleanForAssertion(oracleRoot);

        Assert.Equal(
            oracle.Entries[new ProbeId(1)].Snapshot,
            ProjectProbeSnapshot(harness, rootCapture.Snapshot));
        Assert.Equal(
            oracle.Entries[new ProbeId(2)].Snapshot,
            ProjectProbeSnapshot(harness, childCapture.Snapshot));

        harness.SetTransient(root, 999);
        GeneratedCapture afterTransientMutation = harness.Capture(root);
        Assert.True(harness.DurableEquals(
            rootCapture.Snapshot,
            afterTransientMutation.Snapshot));
        Assert.Equal(
            ProjectProbeSnapshot(harness, rootCapture.Snapshot),
            ProjectProbeSnapshot(harness, afterTransientMutation.Snapshot));
    }

    [Fact]
    public void ProbeTypedEqualityUsesReferenceIdsAndPreservesChildLocality() {
        GeneratorTestRun run = RunGraphProbeGenerator(GraphNodeSource(reorderFields: false));
        AssertSuccessfulProbeRun(run);

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        GeneratedGraphHarness harness = new(assembly.GetRequiredType("Samples.Node"));
        object firstRoot = harness.CreateNode(1, true, 10, 100, "root");
        object firstChild = harness.CreateNode(2, false, 20, 200, "child");
        object secondRoot = harness.CreateNode(1, true, 10, 100, "root");
        object secondChild = harness.CreateNode(2, false, 999, 200, "child");
        harness.SetNext(firstRoot, firstChild);
        harness.SetAlias(firstRoot, firstChild);
        harness.SetNext(secondRoot, secondChild);
        harness.SetAlias(secondRoot, secondChild);

        GeneratedCapture firstParent = harness.Capture(firstRoot);
        GeneratedCapture secondParent = harness.Capture(secondRoot);
        GeneratedCapture firstChildCapture = harness.Capture(firstChild);
        GeneratedCapture secondChildCapture = harness.Capture(secondChild);

        Assert.NotSame(firstChild, secondChild);
        Assert.True(harness.DurableEquals(
            firstParent.Snapshot,
            secondParent.Snapshot));
        Assert.False(harness.DurableEquals(
            firstChildCapture.Snapshot,
            secondChildCapture.Snapshot));

        foreach (object scalarVariant in new[] {
            harness.CreateNode(1, false, 10, 100, "root"),
            harness.CreateNode(1, true, 10, 101, "root"),
            harness.CreateNode(1, true, 10, 100, "ROOT"),
        }) {
            harness.SetNext(scalarVariant, firstChild);
            harness.SetAlias(scalarVariant, firstChild);
            Assert.False(harness.DurableEquals(
                firstParent.Snapshot,
                harness.Capture(scalarVariant).Snapshot));
        }

        harness.SetTransient(secondRoot, 1234);
        Assert.True(harness.DurableEquals(
            firstParent.Snapshot,
            harness.Capture(secondRoot).Snapshot));

        object replacement = harness.CreateNode(3, false, 20, 200, "child");
        harness.SetNext(secondRoot, replacement);
        harness.SetAlias(secondRoot, replacement);
        Assert.False(harness.DurableEquals(
            firstParent.Snapshot,
            harness.Capture(secondRoot).Snapshot));

        harness.SetNext(secondRoot, firstChild);
        harness.SetAlias(secondRoot, null);
        Assert.False(harness.DurableEquals(
            firstParent.Snapshot,
            harness.Capture(secondRoot).Snapshot));
    }

    [Fact]
    public void ProbeDrivenDeltaScenariosMatchTheIndependentOracle() {
        GeneratorTestRun run = RunGraphProbeGenerator(GraphNodeSource(reorderFields: false));
        AssertSuccessfulProbeRun(run);

        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        GeneratedGraphHarness harness = new(assembly.GetRequiredType("Samples.Node"));
        Dictionary<long, object> generatedBaseline = new() {
            [1] = harness.MakeSnapshot(true, 10, 100, "root", 2, 2),
            [2] = harness.MakeSnapshot(false, 20, 200, "child", 1, null),
        };
        NormalizedBaselineGraph oracleBaseline = new(
            new ProbeId(1),
            new Dictionary<ProbeId, BaselineEntry> {
                [new ProbeId(1)] = new(
                    new ProbeSnapshot(10, new ProbeId(2), new ProbeId(2)),
                    RequiresRewrite: false),
                [new ProbeId(2)] = new(
                    new ProbeSnapshot(20, new ProbeId(1), null),
                    RequiresRewrite: false),
            });

        foreach (DeltaScenario scenario in new[] {
            new DeltaScenario(
                ChildId: 2,
                ChildValue: 20,
                RootTransientValue: 0,
                ExpectedUpserts: [],
                ExpectedUnreachable: []),
            new DeltaScenario(
                ChildId: 2,
                ChildValue: 20,
                RootTransientValue: 999,
                ExpectedUpserts: [],
                ExpectedUnreachable: []),
            new DeltaScenario(
                ChildId: 2,
                ChildValue: 21,
                RootTransientValue: 0,
                ExpectedUpserts: [
                    (2, new ProbeSnapshot(21, new ProbeId(1), null)),
                ],
                ExpectedUnreachable: []),
            new DeltaScenario(
                ChildId: 3,
                ChildValue: 20,
                RootTransientValue: 0,
                ExpectedUpserts: [
                    (1, new ProbeSnapshot(10, new ProbeId(3), new ProbeId(3))),
                    (3, new ProbeSnapshot(20, new ProbeId(1), null)),
                ],
                ExpectedUnreachable: [2]),
        }) {
            object generatedRoot = CreateGeneratedScenarioGraph(harness, scenario);
            GeneratedDelta generatedDelta = PlanGeneratedDelta(
                harness,
                generatedBaseline,
                generatedRoot);
            ProbeNode oracleRoot = CreateOracleScenarioGraph(scenario);
            GraphDelta oracleDelta = GraphDeltaProbe.PlanSave(oracleBaseline, oracleRoot);

            Assert.Equal(1, generatedDelta.ResultRootId);
            Assert.Equal(
                scenario.ExpectedUpserts.Select(item => item.Id).Order(),
                generatedDelta.Upserts.Keys.Order());
            Assert.Equal(
                scenario.ExpectedUnreachable.Order(),
                generatedDelta.UnreachableIds.Order());

            foreach ((long id, ProbeSnapshot expectedSnapshot) in scenario.ExpectedUpserts) {
                Assert.Equal(expectedSnapshot, generatedDelta.Upserts[id]);
            }

            Assert.Equal(
                oracleDelta.Upserts.Keys.Select(id => id.Value).Order(),
                generatedDelta.Upserts.Keys.Order());
            Assert.Equal(
                oracleDelta.UnreachableIds.Select(id => id.Value).Order(),
                generatedDelta.UnreachableIds.Order());
            foreach ((ProbeId id, ProbeSnapshot snapshot) in oracleDelta.Upserts) {
                Assert.Equal(snapshot, generatedDelta.Upserts[id.Value]);
            }
        }
    }

    [Fact]
    public void ProbeOutputAndReferenceVisitAreOrderedByFieldId() {
        GeneratorTestRun declaredOrder =
            RunGraphProbeGenerator(GraphNodeSource(reorderFields: false));
        GeneratorTestRun reversedOrder =
            RunGraphProbeGenerator(GraphNodeSource(reorderFields: true));
        AssertSuccessfulProbeRun(declaredOrder);
        AssertSuccessfulProbeRun(reversedOrder);

        string firstGenerated = GeneratedSource(declaredOrder, GraphOperationsHintName);
        string secondGenerated = GeneratedSource(reversedOrder, GraphOperationsHintName);
        Assert.Equal(firstGenerated, secondGenerated);
        Assert.DoesNotContain('\r', firstGenerated);

        Assembly assembly = EmitAndLoad(reversedOrder.OutputCompilation);
        GeneratedGraphHarness harness = new(assembly.GetRequiredType("Samples.Node"));
        object root = harness.CreateNode(1, true, 10, 100, "root");
        object next = harness.CreateNode(2, false, 20, 200, "next");
        object alias = harness.CreateNode(3, false, 30, 300, "alias");
        harness.SetNext(root, next);
        harness.SetAlias(root, alias);

        object[] references = harness.Visit(harness.Capture(root).References);
        Assert.Equal(2, references.Length);
        Assert.Same(next, references[0]);
        Assert.Same(alias, references[1]);
    }

    [Theory]
    [InlineData("array", "Node[]", "_unsupported")]
    [InlineData("base", "IDurableObject?", "_unsupported")]
    [InlineData("cross-type", "Other?", "_unsupported")]
    public void ProbeRejectsUnsupportedReferenceShapes(
        string caseName,
        string fieldType,
        string fieldName) {
        GeneratorTestRun run = RunGraphProbeGenerator(
            UnsupportedReferenceSource(caseName, fieldType, fieldName));

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0007");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(fieldName, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(
            fieldName,
            diagnostic.Location.SourceTree!.GetText()
                .ToString(diagnostic.Location.SourceSpan));
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == GraphOperationsHintName);
    }

    [Fact]
    public void ProbeReservesItsGeneratedOperationsName() {
        string source = GraphNodeSource(reorderFields: false).Replace(
            "    public static long ResolveIdentity(Node value) {",
            "    private static class __DurableGraphOperations { }\n\n" +
            "    public static long ResolveIdentity(Node value) {",
            StringComparison.Ordinal);

        GeneratorTestRun run = RunGraphProbeGenerator(source);

        Diagnostic diagnostic = Assert.Single(
            run.GeneratorDiagnostics,
            candidate => candidate.Id == "DG0018");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(
            "__DurableGraphOperations",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.GeneratedSources,
            generated => generated.HintName == GraphOperationsHintName);
    }

    [Fact]
    public void ProbeCaptureReadsEachReferenceOnceAndKeepsTheNormalPathTyped() {
        GeneratorTestRun run = RunGraphProbeGenerator(GraphNodeSource(reorderFields: false));
        AssertSuccessfulProbeRun(run);
        AssertTypedGeneratedContract(run.OutputCompilation);

        string generated = GeneratedSource(run, GraphOperationsHintName);
        Assert.DoesNotContain("System.Object", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeTag", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Serializer", generated, StringComparison.Ordinal);

        SyntaxTree generatedTree = CSharpSyntaxTree.ParseText(generated, ParseOptions);
        MethodDeclarationSyntax capture = Assert.Single(
            generatedTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "CaptureCurrent");

        AssertReferenceLocalFeedsBothCaptureOutputs(capture, "_next", "Field5");
        AssertReferenceLocalFeedsBothCaptureOutputs(capture, "_alias", "Field6");
    }

    [Fact]
    public void ProductGeneratorSupportsSelfReferencesWithoutRunningTheProbeGenerator() {
        Assert.True(typeof(DurableGraphOperationsProbeGenerator).IsNotPublic);
        Assert.Null(
            typeof(DurableGraphOperationsProbeGenerator)
                .GetCustomAttribute<GeneratorAttribute>());
        GeneratorTestRun run = RunGenerator(SingleReferenceNodeSource());

        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.Contains(run.GeneratedSources, source => source.HintName == "DurableStates.g.cs");
        Assert.DoesNotContain(
            run.GeneratedSources,
            source => source.HintName == GraphOperationsHintName);
    }

    private static GeneratorTestRun RunGraphProbeGenerator(string source) {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"GraphProbeTests_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: PlatformReferences().Append(
                MetadataReference.CreateFromFile(typeof(IDurableObject).Assembly.Location)),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new DurableGraphOperationsProbeGenerator().AsSourceGenerator()],
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

    private static void AssertSuccessfulProbeRun(GeneratorTestRun run) {
        Assert.DoesNotContain(run.GeneratorDiagnostics, IsError);
        Assert.DoesNotContain(run.OutputCompilation.GetDiagnostics(), IsError);
        Assert.Single(
            run.GeneratedSources,
            source => source.HintName == GraphOperationsHintName);
    }

    private static void AssertTypedGeneratedContract(CSharpCompilation compilation) {
        INamedTypeSymbol node = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("Samples.Node"));
        INamedTypeSymbol operations = Assert.Single(
            node.GetTypeMembers("__DurableGraphOperations", arity: 1));
        Assert.Equal(Accessibility.Private, operations.DeclaredAccessibility);
        Assert.True(operations.IsStatic);
        Assert.True(operations.TypeParameters[0].HasValueTypeConstraint);

        INamedTypeSymbol snapshot = Assert.Single(operations.GetTypeMembers("Snapshot"));
        INamedTypeSymbol references =
            Assert.Single(operations.GetTypeMembers("CapturedReferences"));
        Assert.Equal(TypeKind.Struct, snapshot.TypeKind);
        Assert.Equal(TypeKind.Struct, references.TypeKind);

        IMethodSymbol capture = Assert.Single(
            operations.GetMembers("CaptureCurrent").OfType<IMethodSymbol>());
        Assert.True(capture.IsStatic);
        Assert.Equal(4, capture.Parameters.Length);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            node,
            capture.Parameters[0].Type));
        Assert.Equal(RefKind.Out, capture.Parameters[2].RefKind);
        Assert.Equal(RefKind.Out, capture.Parameters[3].RefKind);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            snapshot,
            capture.Parameters[2].Type));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            references,
            capture.Parameters[3].Type));
        INamedTypeSymbol resolver = Assert.IsAssignableFrom<INamedTypeSymbol>(
            capture.Parameters[1].Type);
        Assert.Equal("Func", resolver.Name);
        Assert.Equal(2, resolver.Arity);
        Assert.True(SymbolEqualityComparer.Default.Equals(node, resolver.TypeArguments[0]));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            operations.TypeParameters[0],
            resolver.TypeArguments[1]));

        IMethodSymbol equals = Assert.Single(
            operations.GetMembers("DurableEquals").OfType<IMethodSymbol>());
        Assert.Equal(SpecialType.System_Boolean, equals.ReturnType.SpecialType);
        Assert.Equal([RefKind.In, RefKind.In], equals.Parameters.Select(p => p.RefKind));
        Assert.All(equals.Parameters, parameter => Assert.True(
            SymbolEqualityComparer.Default.Equals(snapshot, parameter.Type)));

        IMethodSymbol visit = Assert.Single(
            operations.GetMembers("VisitReferences").OfType<IMethodSymbol>());
        Assert.Equal(RefKind.In, visit.Parameters[0].RefKind);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            references,
            visit.Parameters[0].Type));
        INamedTypeSymbol visitor = Assert.IsAssignableFrom<INamedTypeSymbol>(
            visit.Parameters[1].Type);
        Assert.Equal("Action", visitor.Name);
        Assert.Single(visitor.TypeArguments);
        Assert.True(SymbolEqualityComparer.Default.Equals(node, visitor.TypeArguments[0]));

        AssertNoWeakObjectFlow(compilation);
    }

    private static void AssertNoWeakObjectFlow(CSharpCompilation compilation) {
        SyntaxTree generatedTree = Assert.Single(
            compilation.SyntaxTrees,
            tree => tree.FilePath.EndsWith(
                GraphOperationsHintName,
                StringComparison.Ordinal));
        SemanticModel semanticModel = compilation.GetSemanticModel(generatedTree);
        SyntaxNode root = generatedTree.GetRoot();

        foreach (TypeSyntax typeSyntax in root.DescendantNodes().OfType<TypeSyntax>()) {
            AssertNotWeakObjectType(
                semanticModel.GetTypeInfo(typeSyntax).Type,
                typeSyntax);
        }

        foreach (ExpressionSyntax expression in root.DescendantNodes().OfType<ExpressionSyntax>()) {
            Microsoft.CodeAnalysis.TypeInfo typeInfo =
                semanticModel.GetTypeInfo(expression);
            AssertNotWeakObjectType(typeInfo.Type, expression);
            AssertNotWeakObjectType(typeInfo.ConvertedType, expression);
        }
    }

    private static void AssertNotWeakObjectType(
        ITypeSymbol? type,
        SyntaxNode node) {
        Assert.False(
            ContainsWeakObjectType(type),
            $"Generated graph operation has weak type '{type}' at '{node}'.");
    }

    private static bool ContainsWeakObjectType(ITypeSymbol? type) {
        if (type is null) {
            return false;
        }

        if (type.SpecialType == SpecialType.System_Object ||
            type.TypeKind == TypeKind.Dynamic) {
            return true;
        }

        if (type is IArrayTypeSymbol arrayType) {
            return ContainsWeakObjectType(arrayType.ElementType);
        }

        if (type is INamedTypeSymbol namedType) {
            return namedType.TypeArguments.Any(ContainsWeakObjectType);
        }

        return false;
    }

    private static void AssertReferenceLocalFeedsBothCaptureOutputs(
        MethodDeclarationSyntax capture,
        string fieldName,
        string generatedFieldName) {
        MemberAccessExpressionSyntax access = Assert.Single(
            capture.DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
            candidate => candidate.Name.Identifier.ValueText == fieldName);
        VariableDeclaratorSyntax local = Assert.IsType<VariableDeclaratorSyntax>(
            access.Ancestors().First(
                ancestor => ancestor is VariableDeclaratorSyntax));
        string localName = local.Identifier.ValueText;
        SeparatedSyntaxList<ParameterSyntax> parameters = capture.ParameterList.Parameters;
        string snapshotParameter = parameters[2].Identifier.ValueText;
        string referencesParameter = parameters[3].Identifier.ValueText;

        AssignmentExpressionSyntax snapshotAssignment = FindFieldAssignment(
            capture,
            snapshotParameter,
            generatedFieldName);
        Assert.Contains(
            snapshotAssignment.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>(),
            identifier => identifier.Identifier.ValueText == localName);

        AssignmentExpressionSyntax referenceAssignment = FindFieldAssignment(
            capture,
            referencesParameter,
            generatedFieldName);
        IdentifierNameSyntax referenceValue =
            Assert.IsType<IdentifierNameSyntax>(referenceAssignment.Right);
        Assert.Equal(localName, referenceValue.Identifier.ValueText);
    }

    private static AssignmentExpressionSyntax FindFieldAssignment(
        MethodDeclarationSyntax method,
        string receiverName,
        string fieldName) {
        return Assert.Single(
            method.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
            assignment => assignment.Left is MemberAccessExpressionSyntax member &&
                member.Expression is IdentifierNameSyntax receiver &&
                receiver.Identifier.ValueText == receiverName &&
                member.Name.Identifier.ValueText == fieldName);
    }

    private static object CreateGeneratedScenarioGraph(
        GeneratedGraphHarness harness,
        DeltaScenario scenario) {
        object root = harness.CreateNode(1, true, 10, 100, "root");
        object child = harness.CreateNode(
            scenario.ChildId,
            false,
            scenario.ChildValue,
            200,
            "child");
        harness.SetNext(root, child);
        harness.SetAlias(root, child);
        harness.SetNext(child, root);
        harness.SetTransient(root, scenario.RootTransientValue);
        return root;
    }

    private static ProbeNode CreateOracleScenarioGraph(DeltaScenario scenario) {
        ProbeNode root = new(new ProbeId(1), 10) {
            TransientValue = scenario.RootTransientValue,
        };
        ProbeNode child = new(new ProbeId(scenario.ChildId), scenario.ChildValue);
        root.Next = child;
        root.Alias = child;
        child.Next = root;
        return root;
    }

    private static GeneratedDelta PlanGeneratedDelta(
        GeneratedGraphHarness harness,
        IReadOnlyDictionary<long, object> baseline,
        object currentRoot) {
        Stack<object> pending = new();
        Dictionary<long, object> instancesById = new();
        Dictionary<long, ProbeSnapshot> upserts = new();
        pending.Push(currentRoot);

        while (pending.Count > 0) {
            object node = pending.Pop();
            long id = harness.Identity(node);
            if (instancesById.TryGetValue(id, out object? existing)) {
                if (!ReferenceEquals(existing, node)) {
                    throw new InvalidOperationException(
                        $"Generated probe identity {id} is used by two CLR instances.");
                }

                continue;
            }

            instancesById.Add(id, node);
            GeneratedCapture capture = harness.Capture(node);
            if (!baseline.TryGetValue(id, out object? baselineSnapshot) ||
                !harness.DurableEquals(capture.Snapshot, baselineSnapshot)) {
                upserts.Add(id, ProjectProbeSnapshot(harness, capture.Snapshot));
            }

            object[] references = harness.Visit(capture.References);
            for (int index = references.Length - 1; index >= 0; index--) {
                pending.Push(references[index]);
            }
        }

        HashSet<long> unreachable = baseline.Keys
            .Where(id => !instancesById.ContainsKey(id))
            .ToHashSet();
        return new GeneratedDelta(harness.Identity(currentRoot), upserts, unreachable);
    }

    private static ProbeSnapshot ProjectProbeSnapshot(
        GeneratedGraphHarness harness,
        object snapshot) {
        return new ProbeSnapshot(
            (int)harness.SnapshotField(snapshot, 2)!,
            ToProbeId(harness.SnapshotField(snapshot, 5)),
            ToProbeId(harness.SnapshotField(snapshot, 6)));
    }

    private static ProbeId? ToProbeId(object? value) {
        return value is null
            ? null
            : new ProbeId((long)value);
    }

    private static void AssertGeneratedSnapshot(
        GeneratedGraphHarness harness,
        object snapshot,
        bool active,
        int value,
        long score,
        string name,
        long? nextId,
        long? aliasId) {
        Assert.Equal(active, harness.SnapshotField(snapshot, 1));
        Assert.Equal(value, harness.SnapshotField(snapshot, 2));
        Assert.Equal(score, harness.SnapshotField(snapshot, 3));
        Assert.Equal(name, harness.SnapshotField(snapshot, 4));
        Assert.Equal(nextId, harness.SnapshotField(snapshot, 5));
        Assert.Equal(aliasId, harness.SnapshotField(snapshot, 6));
    }

    private static string GraphNodeSource(bool reorderFields) {
        string fields = reorderFields
            ? """
                    [DurableField(6)] private Node? _alias;
                    [Transient] private int _transient;
                    [DurableField(4)] private string _name;
                    [DurableField(2)] private int _value;
                    [DurableField(5)] private Node? _next;
                    [Transient] private long _identity;
                    [DurableField(3)] private long _score;
                    [DurableField(1)] private bool _active;
                """
            : """
                    [Transient] private long _identity;
                    [DurableField(1)] private bool _active;
                    [DurableField(2)] private int _value;
                    [DurableField(3)] private long _score;
                    [DurableField(4)] private string _name;
                    [DurableField(5)] private Node? _next;
                    [DurableField(6)] private Node? _alias;
                    [Transient] private int _transient;
                """;

        return $$"""
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType("samples.graph-probe", 1)]
            public sealed partial class Node : IDurableObject {
            {{fields}}

                public Node(
                    long identity,
                    bool active,
                    int value,
                    long score,
                    string name) {
                    _identity = identity;
                    _active = active;
                    _value = value;
                    _score = score;
                    _name = name;
                }

                public static long ResolveIdentity(Node value) {
                    return value._identity;
                }

                public void SetNext(Node? value) {
                    _next = value;
                }

                public void SetAlias(Node? value) {
                    _alias = value;
                }

                public void SetTransient(int value) {
                    _transient = value;
                }
            }
            """;
    }

    private static string UnsupportedReferenceSource(
        string caseName,
        string fieldType,
        string fieldName) {
        string initializer = fieldType.EndsWith("[]", StringComparison.Ordinal)
            ? " = [];"
            : ";";
        return $$"""
            using Atelia.DurableGraph;

            namespace Samples;

            public sealed class Other : IDurableObject { }

            [DurableType("samples.unsupported-{{caseName}}", 1)]
            public sealed partial class Node : IDurableObject {
                [Transient] private long _identity;
                [DurableField(1)] private {{fieldType}} {{fieldName}}{{initializer}}
            }
            """;
    }

    private static string SingleReferenceNodeSource() {
        return """
            using Atelia.DurableGraph;

            namespace Samples;

            [DurableType("samples.product-rejection", 1)]
            public sealed partial class Node : IDurableObject {
                [DurableField(1)] private Node? _next;
            }
            """;
    }

    private sealed class GeneratedGraphHarness {
        private readonly Type _nodeType;
        private readonly Type _snapshotType;
        private readonly ConstructorInfo _nodeConstructor;
        private readonly MethodInfo _resolveIdentity;
        private readonly MethodInfo _setNext;
        private readonly MethodInfo _setAlias;
        private readonly MethodInfo _setTransient;
        private readonly MethodInfo _capture;
        private readonly MethodInfo _durableEquals;
        private readonly MethodInfo _visitReferences;
        private readonly Delegate _identityResolver;

        internal GeneratedGraphHarness(Type nodeType) {
            _nodeType = nodeType;
            _nodeConstructor = Assert.Single(nodeType.GetConstructors());
            _resolveIdentity = nodeType.GetRequiredMethod(
                "ResolveIdentity",
                BindingFlags.Public | BindingFlags.Static);
            _setNext = nodeType.GetRequiredMethod(
                "SetNext",
                BindingFlags.Public | BindingFlags.Instance);
            _setAlias = nodeType.GetRequiredMethod(
                "SetAlias",
                BindingFlags.Public | BindingFlags.Instance);
            _setTransient = nodeType.GetRequiredMethod(
                "SetTransient",
                BindingFlags.Public | BindingFlags.Instance);

            Type openOperations = Assert.Single(
                nodeType.GetNestedTypes(BindingFlags.NonPublic),
                candidate => candidate.Name == "__DurableGraphOperations`1");
            Type operations = openOperations.MakeGenericType(typeof(long));
            const BindingFlags StaticMembers =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            _capture = operations.GetRequiredMethod("CaptureCurrent", StaticMembers);
            _durableEquals = operations.GetRequiredMethod("DurableEquals", StaticMembers);
            _visitReferences = operations.GetRequiredMethod("VisitReferences", StaticMembers);
            ParameterInfo[] captureParameters = _capture.GetParameters();
            _snapshotType = captureParameters[2].ParameterType.GetElementType()!;
            Assert.NotNull(_snapshotType);
            _identityResolver = _resolveIdentity.CreateDelegate(
                captureParameters[1].ParameterType);
        }

        internal object CreateNode(
            long identity,
            bool active,
            int value,
            long score,
            string name) {
            return _nodeConstructor.Invoke([identity, active, value, score, name]);
        }

        internal long Identity(object node) {
            return Assert.IsType<long>(_resolveIdentity.Invoke(null, [node]));
        }

        internal void SetNext(object node, object? next) {
            _setNext.Invoke(node, [next]);
        }

        internal void SetAlias(object node, object? alias) {
            _setAlias.Invoke(node, [alias]);
        }

        internal void SetTransient(object node, int value) {
            _setTransient.Invoke(node, [value]);
        }

        internal GeneratedCapture Capture(object node) {
            object?[] arguments = [node, _identityResolver, null, null];
            _capture.Invoke(null, arguments);
            return new GeneratedCapture(arguments[2]!, arguments[3]!);
        }

        internal bool DurableEquals(object left, object right) {
            return Assert.IsType<bool>(_durableEquals.Invoke(null, [left, right]));
        }

        internal object[] Visit(object references) {
            List<object> visited = new();
            Type actionType = _visitReferences.GetParameters()[1].ParameterType;
            ParameterExpression parameter = Expression.Parameter(_nodeType, "node");
            MethodCallExpression add = Expression.Call(
                Expression.Constant(visited),
                typeof(List<object>).GetMethod(nameof(List<object>.Add))!,
                Expression.Convert(parameter, typeof(object)));
            Delegate visitor = Expression.Lambda(actionType, add, parameter).Compile();
            _visitReferences.Invoke(null, [references, visitor]);
            return visited.ToArray();
        }

        internal object MakeSnapshot(
            bool active,
            int value,
            long score,
            string name,
            long? nextId,
            long? aliasId) {
            object snapshot = Activator.CreateInstance(_snapshotType)!;
            SnapshotFieldInfo(1).SetValue(snapshot, active);
            SnapshotFieldInfo(2).SetValue(snapshot, value);
            SnapshotFieldInfo(3).SetValue(snapshot, score);
            SnapshotFieldInfo(4).SetValue(snapshot, name);
            SnapshotFieldInfo(5).SetValue(snapshot, nextId);
            SnapshotFieldInfo(6).SetValue(snapshot, aliasId);
            return snapshot;
        }

        internal object? SnapshotField(object snapshot, int fieldId) {
            return SnapshotFieldInfo(fieldId).GetValue(snapshot);
        }

        private FieldInfo SnapshotFieldInfo(int fieldId) {
            FieldInfo? field = _snapshotType.GetField(
                $"Field{fieldId}",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            return field;
        }
    }

    private readonly record struct GeneratedCapture(object Snapshot, object References);

    private sealed record GeneratedDelta(
        long ResultRootId,
        IReadOnlyDictionary<long, ProbeSnapshot> Upserts,
        IReadOnlySet<long> UnreachableIds);

    private sealed record DeltaScenario(
        long ChildId,
        int ChildValue,
        int RootTransientValue,
        (long Id, ProbeSnapshot Snapshot)[] ExpectedUpserts,
        long[] ExpectedUnreachable);
}

internal static class GeneratedGraphOperationsReflectionExtensions {
    internal static Type GetRequiredType(this Assembly assembly, string name) {
        Type? type = assembly.GetType(name);
        Assert.NotNull(type);
        return type;
    }

    internal static MethodInfo GetRequiredMethod(
        this Type type,
        string name,
        BindingFlags bindingFlags) {
        MethodInfo? method = type.GetMethod(name, bindingFlags);
        Assert.NotNull(method);
        return method;
    }
}
