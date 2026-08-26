# DurableGraph 实验簿

> 状态：Living Working Note  
> 用途：记录逐步实验形成的认识，帮助作者与 Coding Agent 跨会话恢复项目上下文。  
> 边界：本文不是需求规格、实现指令或当前行为的权威来源；当前源码、测试和可复现输出优先。

## 1. 记录约定

重要陈述使用以下状态：

- **Observed**：已由当前源码、测试或可复现实验直接观察。
- **Decided**：当前采用的工作决定；后续证据允许推翻。
- **Tentative**：有一定依据，但尚未得到足够验证。
- **Rejected**：已经试验或讨论后放弃，并保留放弃原因。
- **Open**：尚待回答的问题。

只记录会影响后续工作的事实、判断和悬案，不复制聊天过程或大段命令输出。

## 2. 当前基线

记录日期：2026-08-27

- **Observed**：仓库目前只有可构建的空项目骨架，没有 DurableGraph 领域实现，也没有实际测试用例。
- **Observed**：`DurableGraph.slnx` 包含 `DurableGraph`、`DurableGraph.Generator`、`DurableGraph.Cli` 和 `DurableGraph.Tests`。
- **Observed**：核心库将 Generator 作为 Roslyn analyzer 引用；CLI 引用核心库；测试项目引用核心库和 Generator。
- **Observed**：运行时项目和测试项目目标框架为 `net10.0`；Generator 为兼容 Roslyn 加载而目标框架为 `netstandard2.0`。
- **Observed**：`Directory.Build.props` 将程序集名、根命名空间和包名统一加上 `Atelia.` 前缀。
- **Observed**：`docs/DurableGraph-target-design-v0.md` 描述目标设计和 P0-P7 原型设想，但尚不代表实现事实。
- **Decided**：近期不进一步细分程序集，也不为了分类整齐而提前整理命名空间。
- **Decided**：采用自底向上的细粒度实验；想清楚一部分、实现和验证一部分，再逐步拼合全景。
- **Decided**：允许后续依据实验结果重构命名、命名空间、项目边界和暂定 API。

## 3. 当前工作地图

### 已有材料

- Target design：提供问题空间、候选不变量和远期方向。
- 空项目骨架：提供最小构建、测试、Generator 和 CLI 边界。
- 本实验簿：保存随实验演化的项目认识。

### 尚未开始

- **Open**：第一个自底向上的关键实验尚未选择。
- **Open**：首个实验应从 durable identity、canonical encoding、Source Generator 最小链路，还是 append/publication 原语开始。
- **Open**：哪些类型和 API 最终属于核心程序集，等待真实代码形状出现后再判断。

## 4. 实验记录模板

后续实验按需增加条目，不要求为了形式填写无意义内容。

```text
### EXP-NNN：名称

状态：Planned / Running / Concluded / Superseded
日期：YYYY-MM-DD

问题：
本轮明确不回答：
最小实验：
成功/失败判据：
观察：
结论：
遗留问题：
相关源码/测试：
```

## 5. 船长日志

### 2026-08-27：建立协作与记录基线

- 完成 .NET 10、xUnit、Source Generator、CLI 和 `.slnx` 空骨架。
- 建立根目录 `AGENTS.md`，将稳定的协作方式与易变的实验认识分开。
- 决定先维护单一实验簿；只有实际内容增长到难以导航时才拆分目录或 ADR。
