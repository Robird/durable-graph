using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atelia.DurableGraph.Tests;

/// <summary>
/// DB-037 G0: executable generated-code equivalents, independent of the product generator.
/// These tests establish CLR/C# shapes; generated and packaged integration need separate tests.
/// </summary>
public sealed class InlineStructShapeWitnessTests {
    [Fact]
    public void RefRestoreWorksForReadonlyPrivateNestedFieldsAndArrayElementsWithoutInitialization() {
        Assembly assembly = Compile(SharedRepresentation + DomainAndRestore);
        Assert.True(Invoke<bool>(assembly, "Shapes.RestoreWitness", "Run"));
    }

    [Fact]
    public void RetainedOwnerUpgradeChainCompilesAndRunsAfterDomainStructDeclarationsDisappear() {
        Assembly original = Compile(SharedRepresentation + DomainAndRestore + OwnerHistory);
        Assembly withoutDomain = Compile(SharedRepresentation + OwnerHistory);

        Assert.NotNull(original.GetType("Shapes.Leaf"));
        Assert.NotNull(original.GetType("Shapes.Links"));
        Assert.Null(withoutDomain.GetType("Shapes.Leaf"));
        Assert.Null(withoutDomain.GetType("Shapes.Links"));
        Assert.Equal(43L, Invoke<long>(original, "Shapes.Owner", "UpgradeOld"));
        Assert.Equal(43L, Invoke<long>(withoutDomain, "Shapes.Owner", "UpgradeOld"));
        Assert.True(Invoke<bool>(withoutDomain, "Shapes.Owner", "DtosAreUnmanaged"));
    }

    private static T Invoke<T>(Assembly assembly, string typeName, string methodName)
        => assembly.GetType(typeName)!.GetMethod(methodName)!.CreateDelegate<Func<T>>()();

    private static Assembly Compile(string source) {
        string platform = Assert.IsType<string>(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "InlineStructShape_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            platform.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray());
    }

    // Shared names stand for SchemaId/Version-derived generated names. No CLR domain names
    // occur in this representation, so a retained owner's history can keep it independently.
    private const string SharedRepresentation = """
        namespace Shapes.Generated {
            public readonly struct LeafV1 {
                public readonly int X;
                public readonly uint NodeId;
                public LeafV1(int x, uint nodeId) { X = x; NodeId = nodeId; }
            }
            public readonly struct LinksV1 {
                public readonly LeafV1 Point;
                public readonly uint TextId;
                public LinksV1(LeafV1 point, uint textId) { Point = point; TextId = textId; }
            }
            public readonly struct LeafV2 {
                public readonly long X;
                public readonly uint NodeId;
                public LeafV2(long x, uint nodeId) { X = x; NodeId = nodeId; }
            }
            public readonly struct LinksV2 {
                public readonly LeafV2 Point;
                public readonly uint TextId;
                public LinksV2(LeafV2 point, uint textId) { Point = point; TextId = textId; }
            }
        }
        """;

    private const string DomainAndRestore = """
        namespace Shapes {
            public sealed class Node { public Node? Self; }
            public static class Counts {
                public static int Constructors;
                public static int Initializers;
                public static int Initialize() { Initializers++; return 99; }
            }
            public readonly partial struct Leaf {
                private readonly int _x;
                private readonly Node? _node;
                private readonly int _transient = Counts.Initialize();
                public Leaf(int x, Node? node) { Counts.Constructors++; _x = x; _node = node; }
                public int X => _x;
                public Node? Node => _node;
                public int Transient => _transient;
            }
            public readonly partial struct Links {
                private readonly Leaf _point;
                private readonly string? _text;
                private readonly int _transient = Counts.Initialize();
                public Links(Leaf point, string? text) { Counts.Constructors++; _point = point; _text = text; }
                public Leaf Point => _point;
                public string? Text => _text;
                public int Transient => _transient;
            }
            public static class ValueHelpers {
                [System.Runtime.CompilerServices.UnsafeAccessor(
                    System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_x")]
                private static extern ref int X(ref Leaf value);
                [System.Runtime.CompilerServices.UnsafeAccessor(
                    System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_node")]
                private static extern ref Node? Node(ref Leaf value);
                [System.Runtime.CompilerServices.UnsafeAccessor(
                    System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_point")]
                private static extern ref Leaf Point(ref Links value);
                [System.Runtime.CompilerServices.UnsafeAccessor(
                    System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_text")]
                private static extern ref string? Text(ref Links value);

                public static Generated.LeafV1 Capture(in Leaf value,
                    System.Func<Node?, uint> registerNode)
                    => new(value.X, registerNode(value.Node));
                public static Generated.LinksV1 Capture(in Links value,
                    System.Func<Node?, uint> registerNode, System.Func<string?, uint> registerText) {
                    Leaf nested = value.Point;
                    return new(Capture(in nested, registerNode), registerText(value.Text));
                }
                public static void Restore(ref Leaf target, in Generated.LeafV1 state,
                    System.Func<uint, Node?> resolveNode) {
                    Leaf value = default;
                    X(ref value) = state.X;
                    Node(ref value) = resolveNode(state.NodeId);
                    target = value;
                }
                public static void Restore(ref Links target, in Generated.LinksV1 state,
                    System.Func<uint, Node?> resolveNode, System.Func<uint, string?> resolveText) {
                    Links value = default;
                    Restore(ref Point(ref value), in state.Point, resolveNode);
                    Text(ref value) = resolveText(state.TextId);
                    target = value;
                }
            }
            public sealed class Holder { public Links Field; }
            public static class RestoreWitness {
                public static bool Run() {
                    Node node = new();
                    node.Self = node;
                    string text = new string(new[] { 'a', 'b' });
                    Links source = new(new Leaf(41, node), text);
                    Generated.LinksV1 state = ValueHelpers.Capture(in source,
                        value => object.ReferenceEquals(value, node) ? 7u : throw new System.Exception(),
                        value => object.ReferenceEquals(value, text) ? 9u : throw new System.Exception());
                    Holder holder = new();
                    Links[] array = new Links[2];
                    // The exact same static helper accepts a field and each existing element slot.
                    ValueHelpers.Restore(ref holder.Field, in state,
                        id => id == 7 ? node : throw new System.Exception(),
                        id => id == 9 ? text : throw new System.Exception());
                    for (int i = 0; i < array.Length; i++) {
                        ValueHelpers.Restore(ref array[i], in state,
                            id => id == 7 ? node : throw new System.Exception(),
                            id => id == 9 ? text : throw new System.Exception());
                    }
                    if (Counts.Constructors != 2 || Counts.Initializers != 2) return false;
                    if (source.Transient != 99 || source.Point.Transient != 99) return false;
                    return Check(in holder.Field, node, text)
                        && Check(in array[0], node, text) && Check(in array[1], node, text)
                        && NoReferences<Generated.LeafV1>() && NoReferences<Generated.LinksV1>();
                }
                private static bool Check(in Links value, Node node, string text)
                    => value.Point.X == 41 && value.Transient == 0 && value.Point.Transient == 0
                        && object.ReferenceEquals(value.Point.Node, node)
                        && object.ReferenceEquals(value.Point.Node!.Self, node)
                        && object.ReferenceEquals(value.Text, text);
                private static bool NoReferences<T>() where T : unmanaged
                    => !System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            }
        }
        """;

    private const string OwnerHistory = """
        namespace Shapes {
            public sealed class Owner {
                public static class State {
                    public readonly struct V1 {
                        public readonly Generated.LinksV1 Links;
                        public V1(Generated.LinksV1 links) { Links = links; }
                    }
                    public readonly struct V2 {
                        public readonly Generated.LinksV2 Links;
                        public readonly byte Added;
                        public V2(Generated.LinksV2 links, byte added) { Links = links; Added = added; }
                    }
                    public readonly struct V3 {
                        public readonly long X;
                        public V3(long x) { X = x; }
                    }
                }
                private static void UpgradeStateV1ToV2(in State.V1 prior, out State.V2 next)
                    => next = new(new(new(prior.Links.Point.X + 1L, prior.Links.Point.NodeId),
                        prior.Links.TextId), 1);
                private static void UpgradeStateV2ToV3(in State.V2 prior, out State.V3 next)
                    => next = new(prior.Links.Point.X + prior.Added);
                public static long UpgradeOld() {
                    State.V1 old = new(new(new(41, 7), 9));
                    UpgradeStateV1ToV2(in old, out State.V2 middle);
                    if (middle.Links.Point.NodeId != 7 || middle.Links.TextId != 9)
                        throw new System.Exception("Lost nested reference IDs during upgrade");
                    UpgradeStateV2ToV3(in middle, out State.V3 current);
                    return current.X;
                }
                public static bool DtosAreUnmanaged()
                    => NoReferences<State.V1>() && NoReferences<State.V2>() && NoReferences<State.V3>();
                private static bool NoReferences<T>() where T : unmanaged
                    => !System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            }
        }
        """;
}
