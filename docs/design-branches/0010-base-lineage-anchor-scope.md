# DB-010：Base lineage anchor 是 per-record 还是 Revision prior snapshot

> 状态：Open
>
> 创建日期：2026-08-29
>
> 当前方向：先保留 per-record Base locator 完成 DB-009 集成；随后验证并优先合并到
> containing Revision 的唯一 OVD parent。Delta direct parent 不在本分叉中。

## 问题

若每个 Save 形成一个 Revision，且全部 records 都从同一个 accepted pre-save snapshot 派生，
那么每条 Base 单独保存 lineage locator 可能与 `Revision.OVD.ParentRevisionTicket` 重复。

候选 A：每条非首 Base 保存自己的 parent Revision locator。

候选 B：Base 不保存 parent；统一使用 containing Revision 的 OVD parent 作为 prior-snapshot
anchor，再按 ObjectId 执行 `LookupLive`。Delta 仍直接保存 exact parent，因为它是 current
reconstruction 热依赖。

## 当前成立的推演

对一个共同 anchor `P`：

- new Base：`LookupLive(P, id)` absent，logical ordinal 1，形成 root；
- domain Base：lookup 得 exact old head，ordinal `+1`；
- maintenance/relocated Base：lookup 得 exact old head，ordinal 与状态相同；
- 不同 objects 的 old heads 可分别位于 A/B；共享的是 prior snapshot，不是同一物理地址；
- relay 形状若保留，relay OVD Self/继承可以让一个共同 relay Revision anchor 分别解析 AA/BA。

当前 simulator 的 single writer、one Save/Revision、每 Save 同 ObjectId 至多一次、禁止 ID reuse
均与该模型一致。尚无 branch merge、historical import 或 rescue consumer。

## 真实表示力差异

共同 anchor 无法表达一个 Revision 中的 Base 来自多个历史 snapshots：

```text
R0: X1, Y1
R1: X2, Remove(Y)
R2: Base(X2 from R1), rescue Base(Y1 from R0)
```

`R2.OVD.Parent=R1` 可解析 X，却在 Y 处命中 Remove。per-record locator 可以分别指 R1.X2 与
R0.Y1。这不是当前反例，但说明候选 B 是明确的单一 prior-snapshot product law，而非纯机械压缩。

Remove 后复用同一 DurableId 并延续旧 lineage 也会要求不同的 historical lookup；当前仍未裁决。

## 最小裁决实验

1. 一个 Revision 同时含 new、domain Base 与 maintenance Base，只给一个 OVD parent；
2. prior OVD 把不同 ObjectIds 解析到 A/B 不同 exact versions；
3. OVD Base 的 live lookup 缺项不继承，与 `ResolveBasePrior` 从 OVD parent 查询严格分离；
4. relay OVD Self 与 empty OVD 分别证明“命中 relay”和“跳过 relay”；
5. Remove 后创建 installed Base fail closed，不静默当作 never-seen root；
6. out-of-snapshot import/rescue 作为明确拒绝案例和重访触发条件。

若上述通过，应删除 Base per-record parent 及其 wire bytes，不增加 direct/locator durable tag。
只有长期并存两种 Base parent 语义时才需要 discriminant；未发布原型不应为过渡兼容冻结它。

## 重访触发条件

- DB-009 的 locator planner 完成 OVD-authoritative materialization；
- branch merge、historical import、rescue restore 或 stale-snapshot Save 成为真实消费者；
- DurableId reuse 的 lineage 语义被裁决。
