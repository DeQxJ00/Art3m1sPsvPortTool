# E-mote PSB PVRTC2 输出约定 / Output contract

本文件供引擎会话实现支持时参考；转换工具不直接修改 GXM 引擎。目前 GXM 的 E-mote `TextureFormat` / `parse_texture` 仅支持 RGBA8、DXT5，PVRTC2 是新的工具输出类型，不宣称原生 Artemis 或现有引擎已支持。

This is a handoff specification, not an engine implementation. The current GXM E-mote loader recognizes RGBA8 and DXT5 only. The following PVRTC2 identifiers are new tool-output types, not a claim of stock Artemis compatibility.

## 类型与数据 / Types and payload

| PSB `source.*.texture.type` | PVRTexLib / PVR pixel code | 原始数据长度 / Raw length |
| --- | --- | --- |
| `DXT5` | 11 (BC3) | `ceil(width/4) × ceil(height/4) × 16` |
| `DXT5_SWIZZLED` | 11 (BC3), custom PSB layout marker | `P(width) × P(height)`; see [layout contract](psb-dxt5-layout.md) |
| `PVRTC2_4BPP` | 5 (PVRTCII 4bpp) | `ceil(width/4) × ceil(height/4) × 8` |
| `PVRTC2_2BPP` | 4 (PVRTCII 2bpp) | `ceil(width/8) × ceil(height/4) × 8` |

- `width` / `height` 是逻辑尺寸；不足整块时仅压缩存储向上补齐，不改逻辑尺寸。
- `pixel` / `data` / `resource` 仍为 PSB 内资源索引。PVRTC2 资源内容是 PVRTexLib 输出的原始压缩块，无 DDS/PVR 头、无 mipmap、未执行 PSV swizzle；只有单独选择的 DXT5 Swizzled 会按新约定重排。
- RGBA 线性色彩空间、非预乘 Alpha；保留 Alpha 通道，但块编码是有损近似。
- PVRTCII 使用标准线性块顺序，不是 PVRTCI 的 Morton 排列；GPU 上传应依据平台格式要求，不能直接套 DXT5 块布局。
- 纹理、icon 采样矩形、origin、画布与动作几何统一跟随全局 Ratio。无需渲染补偿配置或第二次几何缩放。
- PSB 扩展名、内部路径、atlas/source/icon 名称不变。类型字符串和像素资源表随转换重建；其他字符串不被连带修改。

Logical dimensions are retained; only block storage is rounded up. Payloads contain raw linear-color-space, straight-alpha PVRTCII blocks with no container header, mipmaps or PSV swizzle. PVRTCII uses linear block ordering, unlike PVRTCI Morton ordering. GPU upload must follow the target platform's format requirements, not the BC3 block layout. Texture rectangles and all geometry already use the global Ratio; no render compensation or additional scaling is required. PSB paths and atlas/icon names stay unchanged.

## 引擎验收 / Engine acceptance

1. 扩展 E-mote PSB 类型解析及像素长度校验，不仅是 PNG/PVR 加载器。
2. 根据格式接入原生纹理上传；解码回退也必须分辨 2bpp/4bpp，正确保留 Alpha。
3. 验证矩形、多 atlas、透明边缘、非整块及最小尺寸，采样矩形和几何不重复缩放。
4. 确认 Vita / Vita3K 的显示结果和显存分配后再推荐使用；工具测试只证明编码、解码和 PSB 重建，不证明引擎运行效果。

Extend PSB type parsing and payload validation, implement platform upload or a format-aware decoder fallback, and test rectangular/multiple/tiny atlases, alpha edges and non-block-aligned sizes. Validate Vita/Vita3K rendering and memory before recommending PVRTC2. Converter tests verify encoding/decoding and PSB rebuilding, not engine playback.

参考 / References: [Khronos PVRTC data format](https://github.com/KhronosGroup/DataFormat/blob/main/pvrtc.txt), [Imagination PVR header pixel codes](https://docs.imgtec.com/specifications/pvr-file-format-specification/html/topics/pvr-header-format.html).
