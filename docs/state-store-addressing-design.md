# StateStore 地址编码设计

> 状态：Working Design
>
> 更新日期：2026-08-28
>
> 职责：本文是 `RelativeFrameTicket` 与 `AbsoluteFrameAddress` 的地址语义和 wire encoding 单一来源；不决定何时 Rebase、Deltify 或轮转。

## 1. 类型边界

```csharp
readonly record struct AbsoluteFrameAddress(
    uint FileNumber,
    SizedPtr Ticket);

readonly record struct RelativeFrameTicket {
    ulong Encoded { get; }
}
```

`AbsoluteFrameAddress` 是进程内 authority representation。`RelativeFrameTicket` 只在持久字段中出现，不能作为普通 `SizedPtr` 传给 RBF API。

ObjectVersion 的完整地址另带稳定身份：

```text
ObjectVersionAddress
    = AbsoluteFrameAddress + ObjectId
```

`FileNumber` 从 1 开始且单调递增。地址 codec 不决定文件是否存在，也不重复实现 RBF frame validation。

## 2. Origin 语义

relative selector 永远相对于“承载该字段的 Frame 所在文件”：

```text
selector = 0  -> target.FileNumber == origin.FileNumber
selector = 1  -> target.FileNumber == origin.FileNumber - 1
```

`origin` 不是 ambient writable file、当前 PublishedHead 所在文件或 reopen 时被称为 Current 的文件。所有 encode/decode API 都必须显式接收 `originFileNumber`。

因此，同一 raw `RelativeFrameTicket` 在不同 origin 下可能指向不同绝对文件；脱离 origin 的 relative value 没有完整语义。

## 3. Wire encoding

令：

```text
frameCode = SizedPtr.Serialize()
```

required frame ticket 的编码为：

```text
encoded = (frameCode << 1) | selector
```

写入时以 canonical unsigned base-128 VarUInt64 保存 `encoded`。

### 3.1 可表示范围

左移前必须满足：

```text
(frameCode & (1UL << 63)) == 0
```

否则返回 `AddressOutOfRelativeRange`，不得截断最高位。根据当前 `SizedPtr.Serialize()` 位布局，可表示的最大 frame start 为：

```text
MaxRelativeFrameStart
    = ((2^37) - 1) << SizedPtr.AlignmentShift
    = 512 GiB - 4 bytes
```

这是 wire-format 硬边界，不是轮转策略超参数。最后一个合法 Frame 的 end 可以越过该 start 上限，但之后不能再在更大 offset 开始新 Frame。

### 3.2 零值

```text
encoded == 0  -> None
encoded == 1  -> invalid
encoded >= 2  -> required frame ticket
```

`None` 只允许用于 optional parent，例如首个 ObjectVersion。Revision head、ObjectVersionDict live entry 和 required parent 都必须 non-zero。

`encoded == 1` 相当于 `selector=Previous` 且 `frameCode=0`，必须拒绝，避免产生第二种 None 表示。

## 4. Encode

伪代码：

```csharp
RelativeFrameTicket Encode(
    uint originFileNumber,
    AbsoluteFrameAddress target) {
    Require(originFileNumber > 0);
    Require(target.FileNumber > 0);

    ulong frameCode = target.Ticket.Serialize();
    if ((frameCode & (1UL << 63)) != 0) {
        throw AddressOutOfRelativeRange;
    }

    ulong selector;
    if (target.FileNumber == originFileNumber) {
        selector = 0;
    }
    else if (originFileNumber > 1
        && target.FileNumber == originFileNumber - 1) {
        selector = 1;
    }
    else {
        throw AddressHorizonExceeded;
    }

    ulong encoded = checked((frameCode << 1) | selector);
    if (encoded < 2) {
        throw InvalidRequiredFrameTicket;
    }

    return RelativeFrameTicket.FromCanonical(encoded);
}
```

encode 必须拒绝：

- future file；
- 前两代及更老的 file；
- `SizedPtr.Serialize()` 最高位已占用的 frame；
- default/zero `SizedPtr` 作为 required ticket；
- checked arithmetic overflow。

## 5. Decode 与 Resolve

伪代码：

```csharp
AbsoluteFrameAddress Resolve(
    uint originFileNumber,
    RelativeFrameTicket relative) {
    Require(originFileNumber > 0);
    Require(!relative.IsNone);

    ulong encoded = relative.Encoded;
    ulong selector = encoded & 1UL;
    ulong frameCode = encoded >> 1;
    if (frameCode == 0) {
        throw InvalidZeroFrameTicket;
    }

    uint targetFileNumber = selector == 0
        ? originFileNumber
        : checked(originFileNumber - 1);

    if (targetFileNumber == 0) {
        throw AddressHorizonExceeded;
    }

    return new(targetFileNumber, SizedPtr.Deserialize(frameCode));
}
```

wire reader 还必须拒绝 overlong、overflow 或 non-canonical VarUInt。`SizedPtr.Deserialize()` 后是否实际指向存在且完整的 RBF Frame，由 file catalog 与 RBF L3 read 边界裁决。

## 6. ObjectVersionDict 编码规则

内存中的权威映射是：

```text
ObjectId -> AbsoluteFrameAddress
```

读取 ObjectVersionDict chain 时：

```text
for each Map version in source frame:
    for each Upsert(ObjectId, relative):
        absolute = relative.Resolve(sourceFrame.FileNumber)
        authoritativeMap[ObjectId] = absolute
```

写 Map Delta 或 Base 时：

```text
relative = Encode(outputMapFrame.FileNumber, absolute)
```

旧 Map version 中未修改的 entry 先前已经 absolute-normalized；不得把旧 raw relative bits 原样复制到新的 origin。若 target 超出 same/previous horizon，planner 必须先安排对应 ObjectVersion 的 relocation/Rebase；codec 只负责 fail closed，不自行改变图状态。

## 7. VarUInt 派生性质

对同一个非零 `frameCode`：

```text
same     = frameCode << 1
previous = (frameCode << 1) | 1
```

可推导：

- Same 与 Previous 的 canonical VarUInt 长度相同；
- LSB selector 不会让 Previous ticket 固定膨胀为 10 bytes；
- 相比裸 `frameCode`，编码长度只会相同或增加 1 byte；
- 最大 encoded value 为 `ulong.MaxValue`，canonical VarUInt 最多 10 bytes；
- None=0 的 canonical VarUInt 为 1 byte。

这些性质用于 size planner 与测试，不形成第二套 wire authority。

## 8. 单 Frame 自引用 ticket 的布局要求

一个 Revision Frame 内的 ObjectVersionDict 可能指向同 Frame 中刚写出的 ObjectVersion，而最终 `SizedPtr.Length` 又包含这些 self-ticket 的 VarUInt 长度。

writer 必须在真正 append 前完成有界 size preflight：

```text
已知 frame start
    -> 假定 self-ticket VarUInt width
    -> 计算 frame length 与 SizedPtr
    -> 重算 self-ticket width
    -> 直到 width 稳定
```

VarUInt width 只有 1..10，布局随 width 单调不减；若不能稳定、或超过 Frame/TailMeta 上限，则在写入前 fail closed。当前不为了绕过该问题引入额外 `Self` address variant。

## 9. 最小测试向量

- `None` round-trip；
- `encoded=1` 拒绝；
- 小地址 Same/Previous pair；
- VarUInt 1-byte/2-byte 边界；
- 最大允许 frameCode 的 Same/Previous；
- `origin=1 + Previous` 拒绝；
- future、前两代及更老 target 的 encode 拒绝；
- `SizedPtr.Serialize()` bit 63 已占用时拒绝；
- ObjectVersionDict Base 跨文件重写后 relative bits 改变、absolute address 不变；
- self-ticket width fixed-point 的收敛与超限失败。
