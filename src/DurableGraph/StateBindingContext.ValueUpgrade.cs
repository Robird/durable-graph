using System.Reflection;

namespace Atelia.DurableGraph;

public abstract partial class StateBindingContext {
    private const int MaximumValueUpgradeDepth = 256;
    private readonly Dictionary<ValuePlanKey, ValueUpgradePlan> _valueUpgradePlans = [];
    private readonly HashSet<ValuePlanKey> _bindingValueUpgrades = [];

    /// <summary>Gets an explicitly registered rule set from this operation's frozen catalog.</summary>
    public virtual StateValueUpgradeRuleSet GetValueUpgradeRuleSet(Type ruleSet) =>
        throw new InvalidDataException($"No value upgrade rule set is registered for {ruleSet}.");

    private UpgradeDependencies PrepareDependencies(IReadOnlyList<StateUpgradeDependency> declarations,
        DurableSchema? source, DurableSchema? target, int depth) {
        KeyValuePair<string, ValueUpgradePlan>[] plans = new KeyValuePair<string, ValueUpgradePlan>[declarations.Count];
        for (int index = 0; index < declarations.Count; index++) {
            StateUpgradeDependency dependency = declarations[index];
            DurableFieldInfo prior = SelectUpgradeSlot(source, dependency.Source);
            DurableFieldInfo next = SelectUpgradeSlot(target, dependency.Target);
            plans[index] = new(dependency.Key, PrepareValueUpgrade(dependency.RuleSet, prior, next, depth));
        }
        return new(plans);
    }

    private static DurableFieldInfo SelectUpgradeSlot(DurableSchema? schema, StateUpgradeSlot selector) {
        DurableSchema? match = null;
        for (DurableSchema? segment = schema; segment is not null; segment = segment.BaseSchema) {
            if (segment.SchemaId != selector.DeclarationId) { continue; }
            if (match is not null) { throw new InvalidDataException("A value dependency declaration selector is ambiguous in the exact inheritance chain."); }
            match = segment;
        }
        if (match is null) { throw new InvalidDataException($"Value dependency declaration '{selector.DeclarationId}' is absent from its exact endpoint."); }
        foreach (DurableFieldInfo field in match.Fields) {
            if (field.FieldId == selector.FieldId) { return WithFieldId(field, 1); }
        }
        throw new InvalidDataException($"Value dependency field {selector.FieldId} is absent from declaration '{selector.DeclarationId}'.");
    }

    private ValueUpgradePlan PrepareValueUpgrade(Type ruleSet, DurableFieldInfo source, DurableFieldInfo target, int depth) {
        if (depth > MaximumValueUpgradeDepth) { throw new InvalidDataException("Value upgrade dependency binding exceeded its depth bound."); }
        source = WithFieldId(source, 1);
        target = WithFieldId(target, 1);
        ValuePlanKey key = new(ruleSet, source, target);
        if (_valueUpgradePlans.TryGetValue(key, out ValueUpgradePlan? cached)) {
            if (depth + cached.Height - 1 > MaximumValueUpgradeDepth) { throw new InvalidDataException("Value upgrade dependency binding exceeded its depth bound."); }
            return cached;
        }
        if (!_bindingValueUpgrades.Add(key)) { throw new InvalidDataException("Value upgrade dependencies contain a recursive binding cycle."); }
        try {
            StateValueUpgradeRuleSet rules = GetValueUpgradeRuleSet(ruleSet);
            if (rules.RuleSet != ruleSet) { throw new InvalidDataException("The catalog returned a different value upgrade rule set."); }
            StateValueUpgradeProvider[] candidates = rules.Providers.Where(provider => MatchesValueProvider(provider, source, target)).ToArray();
            if (candidates.Length > 1) { throw new InvalidDataException("Multiple explicit value upgrade providers match these nominal endpoints."); }
            // Selectors supply complete endpoints. Validate those layouts directly;
            // value tools never infer a missing Schema or rebuild a shared layout DAG.
            if (source.InlineSchema is { } priorSchema) { BindSchema(priorSchema); }
            if (target.InlineSchema is { } nextSchema) { BindSchema(nextSchema); }
            Type priorType = ResolveStoredValue(source).StateType;
            Type nextType = ResolveStoredValue(target).StateType;
            ValueUpgradePlan plan;
            if (candidates.Length == 0) {
                if (!rules.AllowKeepExact || source != target || priorType != nextType) {
                    throw new InvalidDataException("No explicit value upgrade provider matches, and KeepExact is not available for these complete slots.");
                }
                plan = CreateKeepExactPlan(source, priorType);
            } else {
                StateValueUpgradeProvider provider = candidates[0];
                if ((provider.ExpectedSource is { } expectedSource && expectedSource != source) ||
                    (provider.ExpectedTarget is { } expectedTarget && expectedTarget != target)) {
                    throw new InvalidDataException("The selected value provider expects different complete slot semantics.");
                }
                Dictionary<Type, Type> variables = [];
                UnifyStateType(provider.PriorType, priorType, variables);
                UnifyStateType(provider.NextType, nextType, variables);
                MethodInfo method = CloseUpgradeMethod(provider.Method, variables);
                UpgradeDependencies dependencies = PrepareDependencies(provider.Dependencies, source.InlineSchema, target.InlineSchema, depth + 1);
                plan = CreateValueUpgradePlan(source, target, priorType, nextType, method, dependencies);
            }
            _valueUpgradePlans.Add(key, plan);
            return plan;
        } finally { _bindingValueUpgrades.Remove(key); }
    }

    private static bool MatchesValueProvider(StateValueUpgradeProvider provider, DurableFieldInfo source, DurableFieldInfo target) {
        // Only declared nominal applicability chooses candidates. Signature constraints,
        // expected layouts and dependencies must not hide a broken candidate from KeepExact.
        Dictionary<int, TypeExpr> variables = [];
        return provider.SourceInlineVersion == source.InlineSchema?.Version &&
            provider.TargetInlineVersion == target.InlineSchema?.Version &&
            MatchValuePattern(provider.SourceType, NominalType(source), variables) &&
            MatchValuePattern(provider.TargetType, NominalType(target), variables);
    }

    private static bool MatchValuePattern(TypeExpr pattern, TypeExpr actual, Dictionary<int, TypeExpr> variables) {
        if (pattern.Kind == TypeExprKind.Parameter) {
            if (variables.TryGetValue(pattern.ParameterOrdinal, out TypeExpr? previous)) { return previous == actual; }
            variables.Add(pattern.ParameterOrdinal, actual);
            return true;
        }
        if (pattern.Kind != actual.Kind) { return false; }
        if (pattern.Kind == TypeExprKind.Builtin) { return pattern.BuiltinTag == actual.BuiltinTag; }
        if (pattern.DefinitionId != actual.DefinitionId || pattern.Arguments.Length != actual.Arguments.Length) { return false; }
        for (int index = 0; index < pattern.Arguments.Length; index++) {
            if (!MatchValuePattern(pattern.Arguments[index], actual.Arguments[index], variables)) { return false; }
        }
        return true;
    }

    private static ValueUpgradePlan CreateValueUpgradePlan(DurableFieldInfo source, DurableFieldInfo target,
        Type priorType, Type nextType, MethodInfo method, UpgradeDependencies dependencies) {
        MethodInfo factory = typeof(StateBindingContext).GetMethod(nameof(CreateValueUpgradePlanTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(priorType, nextType);
        return factory.CreateDelegate<Func<DurableFieldInfo, DurableFieldInfo, MethodInfo, UpgradeDependencies, ValueUpgradePlan>>()(
            source, target, method, dependencies);
    }

    private static ValueUpgradePlan CreateValueUpgradePlanTyped<TPrior, TNext>(DurableFieldInfo source, DurableFieldInfo target,
        MethodInfo method, UpgradeDependencies dependencies) where TPrior : unmanaged where TNext : unmanaged =>
        new TypedValueUpgradePlan<TPrior, TNext>(source, target, method.CreateDelegate<UpgradeAction<TPrior, TNext>>(), dependencies);

    private static ValueUpgradePlan CreateKeepExactPlan(DurableFieldInfo slot, Type type) {
        MethodInfo factory = typeof(StateBindingContext).GetMethod(nameof(CreateKeepExactPlanTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);
        return factory.CreateDelegate<Func<DurableFieldInfo, ValueUpgradePlan>>()(slot);
    }

    private static ValueUpgradePlan CreateKeepExactPlanTyped<T>(DurableFieldInfo slot) where T : unmanaged =>
        new TypedValueUpgradePlan<T, T>(slot, slot,
            static (in T prior, out T next, UpgradeContext _) => next = prior, new([]));

    private readonly record struct ValuePlanKey(Type RuleSet, DurableFieldInfo Source, DurableFieldInfo Target);

    private sealed class UpgradeDependencies(KeyValuePair<string, ValueUpgradePlan>[] plans) {
        internal int Height { get; } = plans.Length == 0 ? 0 : plans.Max(static item => item.Value.Height);

        internal UpgradeContext CreateContext(ObjectId objectId, DurableSchema sourceObject, DurableSchema targetObject,
            Dictionary<ValueUpgradePlan, Delegate>? invocationTools = null) {
            invocationTools ??= new(ReferenceEqualityComparer.Instance);
            Dictionary<string, Delegate> tools = new(StringComparer.Ordinal);
            foreach ((string key, ValueUpgradePlan plan) in plans) {
                if (!invocationTools.TryGetValue(plan, out Delegate? tool)) {
                    tool = plan.CreateTool(objectId, sourceObject, targetObject, invocationTools);
                    invocationTools.Add(plan, tool);
                }
                tools.Add(key, tool);
            }
            return new(objectId, sourceObject, targetObject, tools);
        }

        internal void CheckRegistered(StateBindingContext context, HashSet<ValueUpgradePlan> visited) {
            foreach ((_, ValueUpgradePlan plan) in plans) { plan.CheckRegistered(context, visited); }
        }
    }

    private abstract class ValueUpgradePlan(DurableFieldInfo source, DurableFieldInfo target, UpgradeDependencies dependencies) {
        internal int Height { get; } = dependencies.Height + 1;
        protected UpgradeDependencies Dependencies { get; } = dependencies;

        internal void CheckRegistered(StateBindingContext context, HashSet<ValueUpgradePlan> visited) {
            if (!visited.Add(this)) { return; }
            if (source.InlineSchema is { } prior) { context.CheckRegistered(prior); }
            if (target.InlineSchema is { } next) { context.CheckRegistered(next); }
            Dependencies.CheckRegistered(context, visited);
        }

        internal abstract Delegate CreateTool(ObjectId objectId, DurableSchema sourceObject, DurableSchema targetObject,
            Dictionary<ValueUpgradePlan, Delegate> invocationTools);
    }

    private sealed class TypedValueUpgradePlan<TPrior, TNext>(DurableFieldInfo source, DurableFieldInfo target,
        UpgradeAction<TPrior, TNext> action, UpgradeDependencies dependencies) : ValueUpgradePlan(source, target, dependencies)
        where TPrior : unmanaged where TNext : unmanaged {
        internal override Delegate CreateTool(ObjectId objectId, DurableSchema sourceObject, DurableSchema targetObject,
            Dictionary<ValueUpgradePlan, Delegate> invocationTools) {
            // Invocation state is built here, never retained in snapshot plans. Each child
            // receives its own local table while retaining the current owner edge facts.
            UpgradeContext child = Dependencies.CreateContext(objectId, sourceObject, targetObject, invocationTools);
            return new ValueUpgrade<TPrior, TNext>((in TPrior prior) => {
                action(in prior, out TNext next, child);
                return next;
            });
        }
    }
}
