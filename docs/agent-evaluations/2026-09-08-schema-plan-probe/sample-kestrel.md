# 缓存升级计划与共享 Schema 图：替代方案

保留已绑定的 Plan、回调及工具。准备阶段按 **Node 地址**遍历完整物理 DAG，按 **Key**汇总并核对局部声明，另算每个节点的高度；每次执行重新核对当前 Authority，并在用户代码前检查最终 CLR DTO Type。无需重新绑定、登记 Schema 或引入 Authority generation。

草案有四个关键错误：

- 同 Key 就停止下降，隐藏深层异形。例如两个物理根都为 K，均有 `field[1]: Inline(J)`；两个物理 J 的同一字段分别为 `Scalar(i32)`、`Scalar(i64)`。根的 LocalSignature 相同，草案跳过第二个 J。
- 第一次访问时的深度不能代表所有路径。令 `R.field[1] → S → T`，`R.field[2] → A → S → T`，其中 S、T 是共享物理节点，L=3。首次访问 T 的深度为 3；第二次到 S 的深度仍为 3，草案因已见 Key 返回，漏掉深度为 4 的 T。
- Authority 条目的 Key 相同并不证明完整布局相同；永久 `HasValidated` 又会漏掉两次调用之间新登记的冲突条目。
- 最后 `cast<T>` 既晚于回调，也不能表达 CLR Type 精确相等。例如最终 DTO 为 D，`Execute<object>` 的转换可以成功，但合同要求拒绝。

**准备算法。** `Children(n)` 只返回 exact 边，按 Base、FieldId 升序 Inline 排列。`Local(n)` 就采用草案的 LocalSignature；Ref 仅比较完整 closed nominal TypeExpr，不继续遍历。所有 Key/TypeExpr 比较使用结构值相等，物理访问表使用引用相等。

```text
Prepare(boundPlan, L):
    roots = 按步骤顺序列出 source、target；零步时仅 owner
    byKey = 空字典；requirements = 空有序列表
    done = 空的物理节点表

    Visit(n, path):
        if done contains n: return done[n].height
        sig = Local(n)
        if byKey contains n.Key:
            r = byKey[n.Key]
            if r.sig != sig:
                fail("同 Key 异形", n.Key, r.firstPath, path)
            // 相同 Key 的新物理节点仍然必须下降
        else:
            r = (n.Key, sig, path)
            byKey.add(r); requirements.append(r)

        h = 1
        for (edge, child) in Children(n):
            h = max(h, 1 + Visit(child, Extend(path, edge)))
        done[n] = (height = h)
        return h

    for root in roots:
        root.height = Visit(root.node, root.path)
    maxHeight = max(root.height)
    CheckDepth(roots, maxHeight, L)
    return Prepared(boundPlan, roots, done, byKey, requirements,
                    maxHeight, boundPlan.FinalDtoType)
```

零步的 `FinalDtoType` 取已绑定 owner DTO Type。只有成功完成 Prepare 的对象能进入缓存。输入保证无环，因此递归伪代码中的节点不会在自身完成前再次进入；实际实现使用显式栈，避免深输入耗尽调用栈。

`CheckDepth` 在 `maxHeight <= L` 时立即成功；否则选根列表中第一个 `height > L` 的根。从深度 d=1 开始，依次选择第一个满足 `d + height(child) > L` 的子边，直至深度 L+1，报告该路径。L<1 时直接报告根。这样无需枚举重复路径，失败路径也遵循规定的根和子边顺序。即使缓存被不同 L 复用，高度摘要仍有效。

路径保存为“父路径引用 + 边标签”，仅在失败时展开字符串。字典负责查找，有序列表负责诊断顺序；不依赖哈希表枚举顺序。

**每次执行及 Authority 比较。** 局部一致性和高度是不可变 Plan 的已证事实，可以复用；Authority 是否存在条目以及条目布局必须重新检查。

```text
Execute<T>(p, value, Authority, L):
    if typeof(T) != p.FinalDtoType: fail("最终 DTO Type 不符")
    CheckDepth(p.roots, p.maxHeight, L)
    checked = 空的物理节点集合       // 仅本次调用使用

    CheckRegistered(a, origin):
        if checked contains a: return
        if p.byKey 不含 a.Key:
            fail("Authority 布局冲突", origin)
        r = p.byKey[a.Key]
        if Local(a) != r.sig:
            fail("Authority 布局冲突", a.Key, r.firstPath, origin)
        for (_, child) in Children(a):
            CheckRegistered(child, origin)
        checked.add(a)

    for r in p.requirements:
        if Authority.TryGet(r.Key, a):
            if a.Key != r.Key:
                fail("Authority Key 不符", r.firstPath)
            CheckRegistered(a, r.firstPath)

    for step in p.boundPlan.steps:
        value = step.Callback(value)
    return cast<T>(value)
```

`CheckRegistered` 同样使用显式栈。它直接检查已登记 Node 的完整闭包；不能要求其依赖 Key 另有独立的 Authority 条目。`origin` 是触发比较的 Plan 根要求的来源；`r.firstPath` 是冲突 Key 在 Plan 中的来源，二者均可重现。缺失条目允许继续；发现冲突直接失败，保留原 Plan，不换规则、不写 Authority。每个要求只调用一次 `Authority.TryGet`，递归中的查找是 Plan 自己的 `byKey`。

**正确性依据。** 按物理地址去重只省去相同不可变对象的重复工作，每个不同物理声明都会检查。全部同 Key 节点具有相同 LocalSignature 后，其子边 Key 对应相同；对两棵有限展开的最大高度归纳，可得同 Key 节点完整布局相等。因此局部比较可以使用，前提是不能跳过同 Key 的不同物理节点。无需完整布局散列或递归比较每对重复声明。

同理，Authority 闭包内每个实际节点都与 Plan 中对应 Key 的局部声明吻合，就能归纳得到完整布局等价；`checked` 只复用本次已完成的物理节点检查。即使同一 Key 在 Authority 中出现不同物理对象，也会逐个检查。

`height(n)=1+max(height(child))` 与到达 n 的路径无关，根高度正是所有 exact 路径中最大的节点数。因此共享、多根、重复 Key 均不能掩盖超深路径。所有工具端点及递归嵌套工具的依赖，根据题设都包含于 owner 端点的 Base/Inline 闭包；遍历全部 owner 根已覆盖未实际使用的工具。Ref 不增加 exact 依赖或深度。所有可判定的 Schema 检查和合同要求的 DTO Type 相等检查均位于回调循环之前；用户回调本身的行为不属于预检证明。

**复杂度边界。** 设 R 为 owner 根的出现次数、K 为不同 Key 数。按题目对局部标签成本的抽象，准备为期望 `O(N+E+R)` 时间、`O(N+E+R)` 空间；哈希表必须使用完整相等判断解决碰撞，不能仅以散列值判等。若逐个计入 scalar/Ref 字段描述符读取，还应加其总数 F；R 和 F 均不能无条件用 N+E 覆盖。共享 DAG 不展开成路径树。

每次执行做 K 次 Authority 查询。设本次命中的权威条目之闭包共有 A 个不同物理节点、B 条 exact 边，则比较时间为期望 `O(K+A+B)`，临时空间 `O(A)`，字段扫描同样按实际输入量计入。没有权威侧已计算的完整布局摘要时，不能承诺仅 `O(K)`：一个已登记根可以含任意大的尚未读取闭包。每次成功执行的深度检查为 O(1)，失败时才构造见证路径。这些开销均不含回调执行和诊断字符串输出。

**测试。** 使用计数回调和可计数的 Authority 查找器；失败断言检查该次调用的回调增量为零。以下均是可直接实现的构造测试，未声称在实际仓库运行。

| 用例 | 构造及断言 |
|---|---|
| 深层同 Key 异形 | 两个物理 K 根各自引用物理 J，J 分别含 i32/i64。Prepare 拒绝，并给出 J 的两个 source/target 来源路径；重复运行诊断一致。草案漏检。 |
| 共享路径超深 | 使用前述 R/S/T/A 图，L=3。拒绝并定位 `R.field[2] → A → S → T`；L=4 时成功。草案在 L=3 时放行。 |
| 权威深层冲突 | Plan 与已登记 K 的局部签名相同，K 的深层 J 为 i32/i64；Authority 只登记 K。执行拒绝并包含 Plan 来源路径。草案只核对 K。 |
| 缓存后的追加登记 | Authority 为空时执行一次成功；随后合法登记此前未知但布局冲突的 K；再次命中同一 Plan，必须拒绝且本次零回调。草案复用 HasValidated。 |
| DTO 精确相等 | 最终 DTO 为 D，回调返回 D，调用 `Execute<object>`。必须在回调前拒绝；草案回调后转换成功。 |
| 未使用的工具 | 工具端点位于 owner Inline 路径，回调不调用工具；仅该路径中一个深层 Key 的 Authority 布局冲突。仍须拒绝、零回调。草案的 Key 比较放行。 |
| 零步 Plan | owner DTO 为 D，输入为 D，调用 `Execute<object>` 必须拒绝；另以正确 T 配置冲突的 Authority owner，仍须拒绝。草案两者均可放行。 |
| 合法重复与 DAG 规模 | 相等布局由不同物理节点声明同 Key，应成功且每个 Key 每次只查 Authority 一次；再构造 d 层、每层两字段共享下一层的 DAG，L=d。准备只处理 d 个节点及 2(d−1) 条边，不随路径数指数增长。 |
