using Atelia.DurableGraph.Runtime;

namespace Atelia.DurableGraph;

/// <summary>Declares a provider-local tool and the two exact declaration fields it converts.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class UpgradeDependencyAttribute : Attribute {
    public UpgradeDependencyAttribute(string key, Type ruleSet, string sourceDeclarationId, int sourceFieldId,
        string targetDeclarationId, int targetFieldId) {
        Dependency = new(key, ruleSet, new(sourceDeclarationId, sourceFieldId), new(targetDeclarationId, targetFieldId));
    }

    private StateUpgradeDependency Dependency { get; }
    public string Key => Dependency.Key;
    public Type RuleSet => Dependency.RuleSet;
    public string SourceDeclarationId => Dependency.Source.DeclarationId;
    public int SourceFieldId => Dependency.Source.FieldId;
    public string TargetDeclarationId => Dependency.Target.DeclarationId;
    public int TargetFieldId => Dependency.Target.FieldId;
}
