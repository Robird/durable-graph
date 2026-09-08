# 方案：缓存规范化的完整布局要求，每次执行重新对照 Authority

把“Plan 是否自洽”和“本次 Authority 是否仍兼容”分开。前者在 `Prepare` 中一次完成；后者在每次 `Execute` 中完成。缓存不保存“Authority 已验证”这一事实。

## 草案的关键错误

1. `LocalSignature` 加按 Key 提前返回不能证明完整布局相等。例如两个 `K` 都只有 `field[1] -> Inline(C)`；第一个 `C` 含 `Scalar(i32)`，第二个同 Key 的 `C` 含 `Scalar(utf8)`。两个 `K` 的局部签名相等，第二棵子图不会被访问，深层冲突被接受。
2. 按第一次到达深度剪枝会漏掉共享 DAG 的长路径。令 `L=3`，根先经 `field[1]` 在深度 2 到共享节点 `X`，`X` 的叶子在深度 3；同一根随后经 `field[2] -> A -> X`，此时 `X` 深度 3，但因 Key 已见而返回，其叶子实际位于深度 4。草案错误通过。
3. Authority 比较只检查 Key，完全没有比较布局。Plan 的 `K/i32` 会接受 Authority 的 `K/utf8`。
4. `HasValidated` 对可追加的 Authority 无效。第一次执行时 `K` 未登记而通过；随后登记一个冲突的 `K`；第二次执行跳过检查。
5. `cast<T>` 在回调之后才暴露错误。错误的 `T` 已经可能执行全部用户代码，而且“可转换/可强转”也不满足 Type 必须完全相等。

## 缓存结构与准备算法

使用无碰撞语义的规范描述符 `Shape`：

```text
Shape = (Key, Kind, BaseShape?, ordered Fields)
Field = (FieldId, Scalar(code) | Inline(Shape) | Ref(closed nominal TypeExpr))

PreparedPlan {
    Steps                         // 已绑定的回调和全部工具
    InitialDtoType, FinalDtoType
    RequirementsInFirstSeenOrder // (Key, Shape, representative Node, firstPath)
}
```

`Shape` 采用 hash-consing，但哈希只用于找桶；桶内对上述元组逐项精确比较，因此正确性不依赖哈希碰撞概率。子 `Shape` 已规范化后，父描述符可用子描述符身份作精确元组成员。

```text
Prepare(plan, L):
    roots = 每步按顺序列出 source、target
            // 零步则为 (owner, "owner")

    // 确定性 DFS：节点内 Base 优先，再按 FieldId 的 Inline；按物理地址去重。
    // 保存每个物理节点的 firstPath、发现序和后序。
    DiscoverPhysicalDag(roots)

    // DAG 后序动态规划；height 是从该节点到叶子的最大“节点数”。
    for node in postorder:
        height[node] = 1 + max(height[Base/Inline children], default 0)
        longestNext[node] = 取得最大值的第一条边（Base、FieldId 顺序破同值）

    for root in roots:
        if height[root.node] > L:
            fail TooDeep(root.path + Follow(longestNext, L))

    // 子布局先完成，构造完整 Shape；物理节点只处理一次。
    for node in postorder:
        shape[node] = InternExactTuple(node.Key, node.Kind,
                                      shape[node.Base],
                                      ordered scalar/ref/shape-inline fields)

    byKey = empty map
    orderedRequirements = []
    for node in physical nodes by deterministic discovery order:
        if node.Key absent:
            byKey[node.Key] = (shape[node], node, firstPath[node])
            orderedRequirements.add(that entry)
        else if byKey[node.Key].shape != shape[node]:
            fail SameKeyDifferentLayout(byKey[node.Key].firstPath,
                                        firstPath[node])

    ValidateBindings(plan):
        精确检查步骤的相邻 owner、每个回调的输入/输出 DTO、每个已声明工具
        （包括回调未使用的工具）的 source/target 路径和 DTO 绑定；
        路径必须落在相应 owner 两端的 Base/Inline 闭包中。
        生成已检查的调用器，并缓存 InitialDtoType 与 FinalDtoType；
        零步两者均为 owner CLR DTO Type。

    return immutable PreparedPlan
```

题设已经保证工具只能依赖其 owner 端点的 Base/Inline 路径，所以所有工具 Schema 已包含在这些根的闭包中；仍显式枚举并验证每个工具绑定，避免“未被回调使用”成为跳过检查的理由。相邻步骤若用不同物理 Node 表达同一端点，以完整 `Shape` 相等为准，不能用地址或仅用 Key。

深度检查独立于 Key 合并，也独立于首次到达深度。`height(root)` 是该根所有路径长度的最大值，因而一次比较就覆盖该根的每条路径；共享节点对每个根仍会产生正确的最长路径值。确定的根、边和首次发现顺序也使深度错误及同 Key 异形的两条来源路径稳定复现。

## 每次执行与 Authority 比较

```text
Execute<T>(prepared, value):
    if typeof(T) != prepared.FinalDtoType:
        fail WrongResultDtoType
    if RuntimeType(value) 不满足 prepared.InitialDtoType 的既定精确绑定规则:
        fail WrongInputDtoType

    // 本次调用局部；以物理地址 memo，并用与 Plan 相同的精确 Intern 规则。
    authorityCanonicalizer = new Canonicalizer(seed = prepared.ShapeInterner)

    for req in prepared.RequirementsInFirstSeenOrder:
        if Authority.TryGet(req.Key, out registered):
            actual = authorityCanonicalizer.CanonicalizeFullDag(registered)
            if actual != req.Shape:
                fail AuthorityConflict(req.Key, req.firstPath)

    // 到这里，本次所有 Schema、T、输入 DTO 和所有缓存绑定均已检查成功。
    current = value
    for step in prepared.Steps:
        current = step.CheckedCallback(current)
    return (T) current // 由已检查绑定保证，不承担验证职责
```

Authority 未登记某 Key 时按合同接受；已登记则比较包括 Key 在内的完整递归布局。一次执行只对每个不同 Key 调用一次 `TryGet`。规范化 Authority 返回的所有根时共享一个物理节点 memo 和精确 interner，因此即使多个登记根共享依赖，也不会按根到叶路径展开。必须先完成整个 requirements 循环，之后才能调用第一个回调；任何冲突都产生零回调。Authority 两次调用间可能增长，所以没有跨调用的验证布尔值。

准备阶段发现物理 DAG、计算高度、构造描述符各为 `O(N+E)`，空间 `O(N+E)`；按 Key 汇总期望 `O(N)`。它不枚举所有根到叶路径。执行阶段对 `K` 个不同 Key 做 `O(K)` 次 Authority 查询，并对本次实际返回且尚未规范化的 Authority 物理闭包做总计 `O(Na+Ea)` 的精确规范化，空间同阶；若 Authority 自身缓存同一种规范描述符，后者可降为描述符比较，但方案不要求 Authority 增加机制。以上按题意忽略 TypeExpr、标签和诊断字符串成本。

## 区分性测试

1. **深层同 Key 冲突**：构造前述两个局部签名相同的 `K`，差异只在同 Key 子节点的 scalar code。`Prepare` 必须以两条稳定来源路径失败；草案通过。
2. **共享节点的较长路径**：`L=3`，按字段顺序先 `root.field[1] -> X -> leaf`，再 `root.field[2] -> A -> X -> leaf`。必须报告第二条路径过深；草案因已见 `X` 而漏报。
3. **Authority 深布局冲突**：Plan 要求 `K -> Inline(C/i32)`，Authority 登记物理上无关的 `K -> Inline(C/utf8)`。`Execute` 在零回调时失败，并报告 Plan 的来源路径；草案只看 Key 而执行回调。
4. **Authority 追加后重验**：首次执行时 `K` 缺失并成功一次；随后登记冲突 `K`；第二次执行必须失败且该次回调计数仍为零。草案因 `HasValidated` 再次成功。
5. **错误最终 DTO Type**：用可强转的基类或接口作为 `T`，但它不与 `FinalDtoType` 完全相等。必须在任何回调前失败；草案会先产生回调副作用，甚至可能成功强转。
6. **等价重复与零步**：零步 Plan 中放两个物理地址不同、完整布局相同且 Key 相同的声明路径；Authority 登记第三个等价实例。准备和执行均成功，Authority 对该 Key 只查询一次，返回值类型必须精确等于 owner CLR DTO Type。随后改用错误 `T`，必须零回调失败。
