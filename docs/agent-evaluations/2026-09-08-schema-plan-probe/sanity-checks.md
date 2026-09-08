# 题目反例的独立检查

答题 agents 运行期间，编排者用 PowerShell `Add-Type` 临时编译一个独立 C# 小模型，运行以下断言。没有加载产品代码，也没有把此模型提供给答题者。

| 见证 | 草案行为 | 参考算法行为 |
|---|---|---|
| 两个物理 A 声明同 Key，分别包含同 Key 的 B(I32)/B(I64) | 错误接受 | 报同 Key 异形 |
| 先 R→S→T，再 R→A→B→S→T，L=4 | 错误接受 | 报深度越界 |
| 同一共享图，L=5 | 接受 | 接受 |
| 不同物理 A/B 声明，所有字段和 Key 完全等价 | 接受 | 接受 |

观测输出：`4/4 witnesses passed: deep duplicate conflict; shared path depth; inclusive depth boundary; equivalent physical duplicates.`

参考算法按物理节点记忆 height，每个不同物理声明均与 Key 表中的局部签名比较，再按根 height 检查深度。此检查只认证上述四个见证，不是对任意答案算法的完整验证，也不涉及 Authority 或 callback 行为。
