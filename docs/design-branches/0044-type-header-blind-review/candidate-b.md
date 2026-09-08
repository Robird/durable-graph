# 候选 B

## 1. 元数据与身份

SchemaStore 以 `(闭合 nominal 类型表达式, 外层定义版本)` 为逻辑 key 登记完整用户 Schema。
nominal 表达式的节点是基元、带有序实参的用户定义、数组构造，不带任何用户版本。
闭合 Schema 保存成员定义、exact 基类与 inline 值依赖；引用成员只保存不带版本的目标类型约束。
同一个 key 的完整布局若不同则拒绝；key 的相同只在同仓库内保证含义一致。

对象头直接结构化编码逻辑 key：用户定义 ID 字符串、递归实参、外层版本整数。
没有另一个仓库本地紧凑 token 分配层；重复对象可以反复写相同描述。
完整 Schema 与生成历史 DTO/reader 对应；执行能力仍须显式保留，Schema 元数据不生成可执行代码。

## 2. 对象头

使用三类对象头分支：

```text
String: 内建种类标记
User:   closed nominal expression + definition version
Array:  内建 codec 版本 + 数组构造 + 完整元素槽描述
```

元素槽：基元用内建码；struct 用 exact 用户 Schema key；引用用无版本 nominal 约束。
用户 key 经 SchemaStore 查回完整布局；内建 Array 布局由头部直接构造。
nominal 表达式按 prefix 编码：节点 tag、定义字符串/arity（适用时）、子表达式。
整体外层格式版本和种类标记构成解码入口，Type header 后跟 Base body；Delta 继承 Base 解释。

这里统一的是解码后的对象布局概念；字节上保留用户 Schema 和内建数组的不同分支。
字符串长度、arity 和版本使用有界整数编码，具体 token 规则和 malformed 校验需定义。

## 3. 值、引用和泛型

领域 `Pair<int,Point[]>` 的闭合 nominal 身份保留完整参数形状，但 DTO 第二个字段是 ObjectId。
Pair 自身 key 例如 `(Pair<int,Point[]>, v1)`；数组目标自己的头保存 `SZ + inline SchemaKey(Point,v2)`。
引用目标升级而引用边不变时，Pair 自身 Schema/DTO 不变。

`Pair<Point,string>` 的完整闭合 Schema 明确内嵌 Point 的历史布局和 string 引用槽；
仅由无版本 nominal 实参不能推导 Point 版本，须查回 key 对应的完整 Schema。
该 inline 依赖若改变则不能沿用同一个 key 偷换布局，需要 owner 版本变化。

待明确：开放生成模板怎样由完整闭合 Schema 绑定 DTO/reader；phantom 参数及多个领域引用类型投影成同一 ObjectId 时怎样保留 nominal 身份；
缺少所需闭合历史登记或 reader 时的失败边界。
保存的完整定义提供绑定依据，但本材料未把绑定算法写成正式合同。

## 4. 读写路径

写入：捕获领域图及冻结 DTO → 完整 Schema 注册与一致性检查 → 写对象种类及相应 key/数组布局 → 写 Base/Delta。
读取：分派对象头 → 由 SchemaStore 或内建规则取得完整布局 → 绑定历史 reader → 解码 DTO → 独立执行 Upgrade → 恢复领域图。
Schema/reader 可缓存；缓存键及复核须保留完整 exact 依赖。元数据登记应在引用它的 State 发布前持久。

## 5. 需要评价的成本与收益

预期收益是完整闭合布局直接可查、nominal 约束与 exact 值依赖分工明确、不需要独立的整数编号目录。
成本是每对象字符串与递归 key 重复、结构化比较/哈希、用户/内建布局分支，以及多个闭合重复部分元数据。
不同闭合可能共享 exact DAG，进程缓存可减少重复解析/绑定，但不能消除落盘冗余。
是否为该方案增加整数别名或统一外层表达式，属于允许比较的改进项，不能把改进后的性能默认为原候选的事实。
