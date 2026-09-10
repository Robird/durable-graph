using Microsoft.CodeAnalysis;

namespace Atelia.DurableGraph.Generator;

public sealed partial class DurableSchemaGenerator {
    // Metadata supplies nominal identity only. Fixed external inline/base templates still
    // require source-owned history and are rejected by their existing dependency checks.
    private static bool HasExternalDurableNominalShape(INamedTypeSymbol type, Compilation compilation) {
        if (type.DeclaringSyntaxReferences.Length != 0 || type.DeclaredAccessibility != Accessibility.Public ||
            type.ContainingType is not null || type.Arity > 32 || type.IsRefLikeType ||
            (type.IsRecord && type.TypeKind != TypeKind.Struct) ||
            (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct && type.TypeKind != TypeKind.Enum)) return false;
        foreach (ITypeParameterSymbol parameter in type.TypeParameters) {
            if (parameter.AllowsRefLikeType) return false;
        }

        // Resolve the actual referenced contract symbols. Full metadata names alone accept
        // counterfeit attributes or a counterfeit DurableBase from another assembly.
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(DurableTypeAttributeMetadataName);
        if (attributeType is null || attributeType.DeclaringSyntaxReferences.Length != 0 ||
            attributeType.ContainingAssembly.Identity.Name != "Atelia.DurableGraph") return false;
        AttributeData? attribute = GetAttribute(type.GetAttributes(), DurableTypeAttributeMetadataName);
        if (attribute is null || !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType) ||
            attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not string id || string.IsNullOrWhiteSpace(id) || !CanEncodeStrictUtf8(id) ||
            attribute.ConstructorArguments[1].Value is not int version || version <= 0) return false;
        if (type.TypeKind != TypeKind.Class) return true;

        INamedTypeSymbol? durableBase = attributeType.ContainingAssembly.GetTypeByMetadataName(DurableBaseMetadataName);
        if (durableBase is null) return false;
        for (INamedTypeSymbol? ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(ancestor, durableBase)) return true;
        }
        return false;
    }
}
