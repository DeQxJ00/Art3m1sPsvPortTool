# DDS GXMSW 排列约定 / Layout contract

这是转换工具与配套 PSV GXM 引擎之间的私有约定，不是标准 DDS 扩展。不增加标记版本或额外头部；DDS 头仍为 128 字节。此文档不代表引擎已实现支持或通过实机验证。

This is a private converter/PSV GXM engine contract, not a standard DDS extension. No marker version or extra header is added; the DDS header remains 128 bytes. This document does not imply engine support or successful hardware playback.

## 标记位置 / Marker location

下列偏移均从整个文件开头计数，包含四字节 `DDS ` magic。

All offsets include the four-byte `DDS ` magic and are relative to the beginning of the file.

| 字段 / Field | 文件偏移 / Offset | 约定 / Value |
| --- | --- | --- |
| `dwHeight` / `dwWidth` | `0x0C` / `0x10` | 逻辑像素宽高 / Logical pixel dimensions |
| `dwPitchOrLinearSize` | `0x14` | 实际顶层像素资源长度，包括存储补齐 / Actual payload length, including storage padding |
| `dwReserved1` 开头 / start | `0x20`–`0x24` | ASCII `GXMSW`，五个字节 / Five ASCII bytes |
| `ddspf.dwFlags` | `0x50` | `DDPF_FOURCC` (`4`) |
| `ddspf.dwFourCC` | `0x54`–`0x57` | BC1=`DXT1`，BC3=`DXT5`，不改 FourCC / FourCC unchanged |
| 像素数据 / Payload | `0x80` | Swizzled blocks or ordinary Linear blocks |

- 只有 `DXT1` / `DXT5` 配合精确 `GXMSW` 标记才按 GXM Swizzled 读取。
- 没有标记按普通 Linear DDS 处理，不能依据尺寸或数据长度猜测布局。
- 标记使用真正的 `DDS_HEADER.dwReserved1` 保留区；**不写在 FourCC 后面的五个 DWORD 字段**。其余保留字节、RGB 位数和通道掩码保持零。
- 不增加 mipmap，不添加 DDS DX10 头；单张二维纹理，逻辑尺寸均为 1–4096。

Use Swizzled only for `DXT1` / `DXT5` with an exact `GXMSW` marker; unmarked files remain Linear. Do not infer layout from dimensions or payload length. The marker occupies the actual `DDS_HEADER.dwReserved1` area, **not the five DWORD fields after FourCC**. Other reserved bytes, RGB bit counts and masks stay zero. There are no mipmaps or DX10 headers; one 2D texture with logical dimensions 1–4096.

DDS 字段定义参考：[DDS_HEADER](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header)、[DDS_PIXELFORMAT](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-pixelformat)。这些文档不定义 `GXMSW`，该标记仅由本项目约定。

The Microsoft references above define the DDS fields, not this project's private `GXMSW` marker.

## 块排列与尺寸 / Block layout and dimensions

令 `P(n)` 为不小于 `max(4,n)` 的最小 2 的幂。存储网格为 `P(width)/4 × P(height)/4` 个压缩块；BC1 每块 8 字节，BC3 每块 16 字节。

Let `P(n)` be the smallest power of two ≥ `max(4,n)`. The storage grid is `P(width)/4 × P(height)/4` compressed blocks: 8 bytes per BC1 block, 16 bytes per BC3 block.

| 格式 / Format | Linear payload bytes | Swizzled payload bytes |
| --- | --- | --- |
| BC1 / DXT1 | `ceil(width/4) × ceil(height/4) × 8` | `P(width) × P(height) / 2` |
| BC3 / DXT5 | `ceil(width/4) × ceil(height/4) × 16` | `P(width) × P(height)` |

整个压缩块按 **Y-first Morton** 重排；较短边位用尽后继续较长边。块内颜色、Alpha 和像素索引完全不变。补齐区域为零，逻辑宽高不变，不能把补齐尺寸当作绘制尺寸。重排方法与 [PSB DXT5 排列约定](psb-dxt5-layout.md) 相同，仅 BC1 块字节数不同。

Reorder whole blocks in **Y-first Morton** order, continuing the longer dimension after the shorter ends. All block-internal bits remain unchanged. Padding is zero; logical dimensions must not be replaced with storage dimensions. The algorithm matches the linked PSB contract, with 8-byte BC1 blocks instead of 16-byte BC3 blocks.

例如 960×540：Linear BC1 为 259,200 字节，Swizzled BC1 为 524,288 字节；Linear BC3 为 518,400 字节，Swizzled BC3 为 1,048,576 字节。此为像素资源，不含 128 字节文件头或 GPU 分配对齐。Swizzled 可能增大文件，但预先完成 GPU 所需重排，不是另一种更小的压缩算法。

For 960×540, Linear/Swizzled payloads are 259,200/524,288 bytes for BC1 and 518,400/1,048,576 bytes for BC3, excluding headers and GPU-allocation alignment. Swizzling can increase file size; it precomputes GPU block ordering rather than providing a smaller compression algorithm.

## 选择与引擎接入 / Selection and engine handoff

- 显式 BC1 / BC3 均提供 Swizzled（排在前）和 Linear；两组 AUTO 也各提供两种，默认推荐 Swizzled。
- Swizzled AUTO：不透明彩色图用 BC1 Swizzled，透明彩色图用 BC3 Swizzled。Linear AUTO 用相应的线性格式。普通 AUTO 仍保留 UI / 未知目录；手动“排除偏移信息”模式仅对选中分类放宽目录限制。灰度、附加定位信息、已有原生纹理、冲突和无法解析的图片仍按原规则保护。
- 仅 BC1 / BC3 的 DDS 新增该排列。BC2 / BC4 / BC5、PVRTC / ETC 输出规则不变；PSB 排列选项独立控制 PSB。
- 引擎先识别标记并校验资源长度，再复制到正确对齐且 GPU 可访问的内存；标记文件不可再次 swizzle。未标记文件维持原线性加载/重排流程。
- CPU 解码和要求线性块的后端先反重排，并去掉存储补齐。使用 GXM 时以逻辑宽高初始化匹配格式的 SwizzledArbitrary 描述符。
- 未接入标记的旧引擎或普通 DDS 工具可能忽略标记并错误显示，须选 Linear。需另行验证实机/Vita3K，转换测试不替代运行时显示测试。

Explicit BC1/BC3 and both AUTO families offer Swizzled first and Linear second. Default AUTO chooses Swizzled BC1 for opaque color and Swizzled BC3 for alpha. Linear AUTO chooses the corresponding linear formats. Existing grayscale, metadata, native-resource and conflict protection remains intact. BC2/BC4/BC5 and PVR output are unchanged; the PSB selector is separate.

The engine must detect the marker, validate the matching payload length and upload into appropriately aligned GPU-accessible memory **without a second swizzle**. Keep the original loading path for unmarked Linear DDS. CPU decoding and linear-block backends must unswizzle and remove padding. Initialize GXM SwizzledArbitrary with logical dimensions and the corresponding BC format. Legacy engines and ordinary DDS readers may ignore the marker and render incorrectly; use Linear until supported and validate actual Vita/Vita3K playback.
