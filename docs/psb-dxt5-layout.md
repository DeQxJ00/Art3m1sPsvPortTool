# PSB DXT5 排列约定 / Layout contract

转换工具提供两种 DXT5 排列，默认 Swizzled。仅修改转换工具，不直接修改 GXM 引擎。当前 E-mote PSB 加载器尚不识别新的类型标记；配套支持前可选择 Linear。

The converter provides two DXT5 layouts, defaulting to Swizzled. This is not an engine patch. The current E-mote PSB loader does not recognize the new marker; use Linear until matching support is implemented.

| `source.*.texture.type` | 排列 / Layout | 数据长度 / Payload length |
| --- | --- | --- |
| `DXT5` | Linear, row-major 4×4 BC3 blocks | `ceil(width/4) × ceil(height/4) × 16` |
| `DXT5_SWIZZLED` | GXM rectangular Morton, Y bits first | `P(width) × P(height)` |

`P(n)` 为不小于 `max(4,n)` 的最小 2 的幂。Swizzled 逻辑宽高限制为 1–4096，与当前 GXM BC3 上传路径一致；资源不带 DDS/PVR 头，不含 mipmap，也不包含 256 KiB GPU 内存分配对齐。

`P(n)` is the smallest power of two ≥ `max(4,n)`. Swizzled logical dimensions are limited to 1–4096, matching the current GXM BC3 upload path. Payloads have no DDS/PVR header, mipmaps or 256 KiB GPU-allocation padding.

## 重排规则 / Block ordering

- 对完整 16 字节 BC3 块重排，不调整块内部 Alpha、颜色或索引。
- 存储块网格 `BW=P(width)/4`、`BH=P(height)/4`。
- 每一位先写 Y（该位小于 BH 时），再写 X（该位小于 BW 时）；较短维度结束后只继续另一维度。
- 线性源偏移为 `(y × ceil(width/4) + x) × 16`，目标偏移为 `MortonYFirst(x,y,BW,BH) × 16`。
- 仅实际块参与复制，存储补齐区域为零；逻辑 width/height、truncated 尺寸、icon 和几何仍只跟随全局 Ratio。
- 已有线性/Swizzled DXT5 的纯排列互转只搬移块，不重新有损编码；再次缩放时先反重排，再解码缩放。

Reorder whole 16-byte BC3 blocks, leaving all bits inside each block unchanged. Interleave Y before X while each dimension has bits left, then continue only the longer dimension. Copy actual blocks and zero the padded storage. Logical dimensions and geometry still follow only the global Ratio. Layout-only conversion is lossless; resizing first unswizzles, then decodes and resizes.

4×4 块网格（16×16 像素）线性块编号在输出中的顺序 / Golden block order:

```text
0,4,1,5,8,12,9,13,2,6,3,7,10,14,11,15
```

这对应 GXM 工程 `host-direct/src/bc3_layout.hpp` 中的块地址规则。它不是 X-first Morton、PSP 字节平铺或任意“swizzle”算法。

This matches the block addressing in the GXM project's `host-direct/src/bc3_layout.hpp`, not X-first Morton, PSP byte tiling or an unspecified swizzle algorithm.

## 引擎接入 / Engine handoff

1. E-mote PSB 解析同时识别 `DXT5` 和 `DXT5_SWIZZLED`，校验各自长度。不可把两者无条件当作同一布局。
2. Linear 继续走原有 swizzle 上传路径。Swizzled 数据可按约定复制到 GPU 纹理存储，再初始化 GXM SwizzledArbitrary BC3 描述符；**不能再次 swizzle**。
3. CPU RGBA 解码、OpenGL/其他线性 BC3 后端先反重排，去掉补齐块；矩形需按上述 Y-first 规则处理。
4. 验证 1×1、非整块、非 2 的幂、横/竖矩形、多 atlas 和透明边缘，以及 Vita/Vita3K 上的实际显示。

Parse and validate the two layouts distinctly. Keep the old swizzle-upload path for Linear. Copy Swizzled payloads to matching GXM storage without a second swizzle. CPU decoding and linear-BC3 backends must unswizzle and discard padding first. Validate actual Vita/Vita3K rendering; converter golden-order and round-trip tests do not replace engine playback tests.
