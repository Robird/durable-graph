using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelia.DurableGraph.Tests;

public sealed partial class DurableSchemaGeneratorTests {
    private static void AssertGeneratedBodiesRemainStaticallyBound(string generated) {
        var root = CSharpSyntaxTree.ParseText(generated, ParseOptions).GetRoot();
        foreach (TypeOfExpressionSyntax typeOf in root.DescendantNodes().OfType<TypeOfExpressionSyntax>()) {
            MethodDeclarationSyntax owner = Assert.Single(typeOf.Ancestors().OfType<MethodDeclarationSyntax>());
            Assert.Equal("Allocate", owner.Identifier.ValueText);
            ArgumentSyntax argument = Assert.IsType<ArgumentSyntax>(typeOf.Parent);
            ArgumentListSyntax arguments = Assert.IsType<ArgumentListSyntax>(argument.Parent);
            InvocationExpressionSyntax invocation = Assert.IsType<InvocationExpressionSyntax>(arguments.Parent);
            Assert.Equal("global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject", invocation.Expression.ToString());
            Assert.Equal(typeOf.Span, Assert.Single(arguments.Arguments).Expression.Span);
        }

        MethodDeclarationSyntax[] bodies = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "Write" or "PrepareBase" or "PrepareDelta" ||
                method.Identifier.ValueText.StartsWith("ReadV", StringComparison.Ordinal) ||
                method.Identifier.ValueText.StartsWith("ApplyDeltaV", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(bodies);
        Assert.Contains(bodies, method => method.Identifier.ValueText == "Write");
        Assert.Contains(bodies, method => method.Identifier.ValueText.StartsWith("ReadV", StringComparison.Ordinal));
        foreach (MethodDeclarationSyntax method in bodies) {
            string body = method.ToString();
            foreach (string forbidden in new[] {
                "typeof", "GetType(", "GetUninitializedObject", "Upgrade", "Normalize",
                "ValueSlotCodec", "PrimitiveSlotCodecs", "System.Reflection", "DynamicInvoke", "Dictionary<",
            }) {
                Assert.DoesNotContain(forbidden, body);
            }
        }
    }
}
