# 设计题 v1：缓存的升级计划与共享 Schema 图

这是一道自包含的假想库设计题。只使用本文；不需要查任何实际仓库实现、历史记录或外部材料。提交简短的工程论证，不要求报告内部思考过程。

## 模型与合同

一个不可变的 `Node` 表示一份 exact Schema 声明：

```text
Node {
    Key: (closed TypeExpr, Version)
    Kind: ReferenceObject | InlineValue
    Base: Node?                     // exact edge
    Fields: ordered (FieldId, Slot)  // FieldId 唯一、升序
}
Slot = Scalar(code) | Inline(Node) | Ref(nominal TypeExpr)
```

closed TypeExpr 是包含全部泛型实参的结构值。对象地址相同意味着同一个 Node；Key 相同不意味着对象地址或完整布局相同。完整布局等价要求 Kind、字段 ID/次序、scalar code、Ref 的 nominal TypeExpr、Base/Inline 的完整布局递归相等。Key 也是布局比较的一部分。Ref 只有类型约束，没有 exact 目标版本，也不提供 Node 指针。

输入 Node 指针图保证不可变、有限、无环；允许大量共享，也允许不同物理节点声称同一个 Key。后者可能相等，也可能在很深的子字段中冲突。不能假定输入已经通过一致性验证。

一个 `Plan` 包含已绑定的相邻 owner 升级步骤 `v1->v2->...->vn`，每步有 source/target exact Node、目标 CLR DTO Type 和一个用户回调。每步还声明零个或多个值转换工具，工具的 source/target 必须来自该步两个 owner 端点的 Base/Inline 字段路径；嵌套工具递归受相同约束，不可能另有路径外的 Schema 依赖。工具可以声明而不被回调实际使用。整个 Plan 在执行任何用户代码前已绑定完整，缓存中不会出现半成品。零步 Plan 也有一个 owner Node 和其 CLR DTO Type。

`Authority` 是外部权威表 `Key -> 完整 Node`。它单线程使用；一次 `Execute<T>` 期间没有修改或重入，但两次调用之间可以追加登记。登记允许此前未知的 Key，拒绝改变已经登记的 Key 的布局；每个已登记条目的完整 exact 依赖闭包已通过一致性验证。Plan 可以从代码推导出尚未登记的 Node。读取或执行 Plan 不自动登记 Schema；缺少权威条目可以接受，但已有条目与 Plan 要求冲突必须拒绝。

要求：

1. `Execute<T>` 在该次调用的第一个用户回调前完成所有 Schema/DTO 类型检查；不能只检查实际执行到的步骤或用到的工具。错误时该次调用零回调；不能悄悄丢弃缓存、换一条升级规则或修改 Authority。
2. `T` 必须等于 Plan 的最终 CLR DTO Type；无须再绑定一次模型来得到这个 Type。Schema Key、DTO Type、Node 地址是不同的等价关系。
3. exact 路径深度上限为 `L`，owner 根深度为 1，只计 Base/Inline 边。每一个 owner 端点到任一叶子的路径都必须满足上限；共享节点、多个步骤和缓存命中不豁免这项要求。
4. 多个根按步骤顺序、source 后 target 遍历；节点内部先 Base 再按 FieldId 遍历 Inline。错误诊断必须可重现，同 Key 异形至少给出两个来源路径；与 Authority 冲突至少给出 Plan 内一个来源路径。完整打印所有重复路径不在需求内。
5. 准备阶段可以为输入物理 DAG 做线性量级预处理。避免把 DAG 按所有根到叶路径展开成树；相同 Key 的等价重复声明不应导致执行期重复查 Authority。忽略 TypeExpr、字段标签和诊断字符串比较成本时，希望 Schema 检查预处理接近 `O(N+E)`：N/E 指所有 owner 端点可达的不同物理节点/边。执行期可以线性扫描不同 Key 的要求。Authority 提供的 Node 与 Plan Node 不保证是同一物理对象。

## 待评审草案

工程师建议缓存 Plan 时做以下预处理；`LocalSignature` 比较 Kind、字段标签、scalar code、Ref 类型，以及 Base/Inline 子节点的 Key，但不递归比较子布局：

```text
requirements = Dictionary<Key, Node>()
maxDepth = L

Visit(node, depth, path):
    if depth > maxDepth: fail("too deep", path)
    if requirements.TryGet(node.Key, old):
        if LocalSignature(old) != LocalSignature(node):
            fail("conflict", old.firstPath, path)
        return
    requirements.Add(node.Key, node, path)
    if node.Base != null: Visit(node.Base, depth + 1, path + ".base")
    for each Inline field in node.Fields:
        Visit(field.Node, depth + 1, path + ".field[id]")

Prepare(plan):
    for each step:
        Visit(step.Source, 1, stepPath + ".source")
        Visit(step.Target, 1, stepPath + ".target")
    // zero-step 时 Visit(plan.Owner, 1, "owner")
    plan.Requirements = requirements

Execute<T>(plan, value):
    if !plan.HasValidated:
        for each requirement in plan.Requirements:
            if Authority.TryGet(requirement.Key, registered):
                if registered.Key != requirement.Key: fail("mismatch")
        plan.HasValidated = true
    for each step:
        value = step.Callback(value)
    return cast<T>(value)
```

## 你的交付

设计一个替代上述草案的最小可维护方案，保留已绑定回调与工具。请提供：

- 草案的关键错误及具体反例；
- 准备、缓存命中执行、权威比较的核心伪代码或精确算法；
- 为什么它覆盖完整依赖、所有深度路径和零回调失败要求，以及复杂度边界；
- 至少四个能区分正确实现与上述草案的测试。

两份候选实现可以有不同形状。不要为未来的并发、Schema 登记 generation、热更新、持久格式或业务图读取增加机制。篇幅建议在 2,500 个中文字符左右，必要时可以略长；得分依赖正确性与可检查证据，不依赖长度或自信程度。
