# Codex Goal：完成阶段 A G0-G4

> 状态：Completed Historical Work Order / Do Not Rerun
>
> 最近校准：2026-09-02
>
> 适用范围：`experiments/MultiSegmentStateStoreProbe`

> 历史说明：下方 prompt 记录完成 G0-G4 时的施工合同，其中 candidate-crossing/rerender placement 已由
> 后续 tail-triggered rollover 决策取代。当前实现与规范以 `TARGET-DESIGN.md`、`PROJECT-STATE.md` 和源码
> 为准；不要再次粘贴执行本 Goal。旧完整可执行状态保存在 commit
> `556365b0043519b3ff94a98a26818495ff327986`。

## 使用说明

OpenAI 官方的 [Follow a goal](https://learn.chatgpt.com/use-cases/follow-goals) 说明 `/goal` 适合有清晰
objective、验证循环和停止条件的长期工作；可用 `/goal` 查看状态，并用 `/goal pause`、`/goal resume`、
`/goal clear` 控制。若命令未出现，可启用：

```toml
[features]
goals = true
```

或运行：

```text
codex features enable goals
```

官方 [Developer commands](https://learn.chatgpt.com/docs/developer-commands#cli-set-or-view-a-task-goal-with-goal)
规定 objective 必须非空且不超过 4,000 字符。下方 prompt 已控制在该限制内；不要再在 `/goal` 后附加大段
阶段 B 需求。修改现有 objective 用 `/goal edit`；彻底替换目标时先 `/goal clear` 再新建；`pause` 只暂停
当前 Goal，不允许同时创建另一个未完成 Goal。

本机 2026-09-02 的 Codex 构建观察到：Goal 会在 thread idle 时自动续跑；agent 只能把 Goal 标为
`complete` 或 `blocked`，pause/resume/clear 由用户或系统控制。`complete` 必须基于当前证据逐项审计；
`blocked` 只适用于同一个真实阻塞连续至少三个 Goal turns 且已无其他有意义进展的情况。不要设置未经用户
明确要求的 token budget。

## 可直接粘贴的 Prompt

```text
/goal 完成 `experiments/MultiSegmentStateStoreProbe` 的阶段 A（In-Memory Probe，截止 G4）。唯一目标是按当前设计把 G0-G4 全部做成可执行证据；停止条件是下述 gates、最终验证、文档收口和 Git 提交全部成立。不要进入阶段 B。

开始前完整读取并遵守仓库根 `AGENTS.md`、`experiments/MultiSegmentStateStoreProbe/PROJECT-STATE.md` 与 `TARGET-DESIGN.md`。权威顺序：当前用户指令和 AGENTS.md > 当前源码、测试及实际命令输出（实现事实）> TARGET-DESIGN.md（阶段 A 目标规范）> PROJECT-STATE.md（活动导航）> README.md。DB-014 可解释路线；`STATESTORE-SUBSYSTEM-DESIGN.md` 只用于识别阶段 B 边界。先检查 `git status`，保留既有用户改动，不 reset、checkout、覆盖或夹带无关文件。G0 已有证据时先复核，不重复重写。

依次闭合以下 gates，并保持依赖顺序：
G0 地址：1-based checked FileNumber、canonical filename、distance 0/1/>65535/max round-trip、canonical VarUInt 和 underflow/future/zero-ticket fail-close。
G1 Frame store/rollover：强类型 FrameTicket 与 same-file strictly-earlier；in-memory append-only Segment/FileStore；nonempty crossing、empty oversize、hard-bound reject；rollover 不重跑 logical policy，换 origin 后重新 relativize/render/measure；reject 不 publish。
G2 reconstruction：OVD Base/Delta、BindSelf/External/Remove 与 ObjectVersion Base/Delta；F1 冷 Base 留存而热对象/Revision 推进至 F4；F4 exact current state 可 materialize；required missing、future、same-file non-earlier、cycle、错误 ObjectId fail closed。
G3 planning/apply/lineage：exact-parent normalization；origin-free RevisionPlan 与唯一 whole-candidate estimator；OVD Base 在新 origin 重编码历史 head 而不 relocation；non-genesis shared PriorRevision；最小 Base lineage witness；append-before-publish failure 只留下不可见 candidate。
G4 workload/policy/evaluator：移植 deterministic workload/generator/composer；实现精简 ReadAmplification + BaseBudget；用独立 A/B-free trace 证明 SameStateRebase ordinal 不变、增加一次 W 且降低后续累计 R；准确报告 W/P/F/R/L、Delta/Base references 与 shared-Frame cold-read 去重；同一 workload 输出 all-Delta/all-Base/adaptive raw outcomes，不做总分、排行榜或 winner 声称。

每个 gate 使用同一闭环：先写明本切片问题和最小可观察成败条件；检查冻结的 TwoLegRotationProbe 中同领域代码/测试，只复制或改写最小思想，不建 project dependency，不带回 A/B/C、rotation、A-debt、evacuation、paired candidate 或 terminal settlement；实现最小 coherent slice 和 focused tests；运行相关测试；检查实际 diff；只在证据改变模型、roadmap 或开放问题时压缩更新 PROJECT-STATE.md，并按 AGENTS.md 更新 lab notebook/相关设计说明。文档不得把计划写成已实现。

允许自主新增、删除、重构 Probe 内的代码/测试/文档，运行诊断、测试、format/build，修复由本阶段改动暴露的问题，并可派 bounded subagents 做机械实现或独立复核；但主线程必须检查实际 diff 并重跑验证。若一个选择会实质改变实验、冻结 durable format/public API 或越过现有设计，先列替代方案、证据与推荐，不擅自扩大目标。

严格禁止：真实 filesystem/RBF reopen、durable flush/head carrier/crash recovery、旧 G5/G6、`src/DurableGraph` 产品整合、EventJournal/RbfSegmentStore/NuGet/ProjectReference 接入、正式 wire/API、GC/compaction/删除历史文件、multi-writer，以及把整个 Probe 搬入产品。不要为了容易通过测试而缩小终态；也不要实现阶段 B 占位抽象。

持续维护简短 plan，完成一项就更新；进度报告只写当前 checkpoint、已验证证据、剩余项和真实 blocker。每个 gate 后运行 focused tests；最终至少运行：
`dotnet test experiments/MultiSegmentStateStoreProbe/MultiSegmentStateStoreProbe.slnx --no-restore --verbosity minimal`
`dotnet build experiments/MultiSegmentStateStoreProbe/MultiSegmentStateStoreProbe.slnx --no-restore --verbosity minimal`
`dotnet format experiments/MultiSegmentStateStoreProbe/MultiSegmentStateStoreProbe.slnx --no-restore --verify-no-changes --verbosity minimal`
`dotnet build DurableGraph.slnx --no-restore --verbosity minimal`
若 restore 状态导致 `--no-restore` 失败，先执行正常 restore/build/test 再重跑等价验证。最终还要有 deterministic corpus 的三种 admitted raw report，以及 F1-F4、rollover re-render、OVD Base external head、SameStateRebase 和主要 fail-close 反例证据。

Commit 纪律：每个完成 gate 或紧密相关切片先检查 diff/tests，再做可恢复、单一主题 commit；只提交本阶段有意文件，不 amend 用户 commit，不把未验证或无关变化混入。最后把 PROJECT-STATE.md 收口为“G0-G4 complete / awaiting promotion decision”，README 与 lab notebook 只陈述已证实事实，并确认工作树没有本目标遗留的未提交改动（既有无关用户改动保持原样）。

只有逐项 completion audit 证明所有上述要求满足、验证通过、文档与 commits 收口且没有阶段 A 必需工作剩余时，才调用 update_goal(status=complete) 并停止。困难、缓慢、不确定、局部失败、预算将尽或希望澄清都不算 blocked：先重验、换安全路径并推进其他可做项。只有同一个真实阻塞条件连续出现在至少三个 goal turns（含自动续跑），且确实必须等待用户输入或外部状态、已无任何有意义进展可做时，才调用 update_goal(status=blocked)；首次或第二次只报告并保持 goal active。
```

## 结束后的下一步

Goal 完成后先由用户审阅 G0-G4 evidence 和 raw reports。只有用户明确启动阶段 B，才按
[`STATESTORE-SUBSYSTEM-DESIGN.md`](STATESTORE-SUBSYSTEM-DESIGN.md) 调查 Atelia substrate、正式 persistence
与 DurableGraph integration；不要复用本 Goal 继续自动推进。
