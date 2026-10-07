# 更新日志 / Changelog

本文件按应用版本记录用户可见的功能和重要修复。日期采用北京时间；历史内容依据仓库提交与标签整理。发布新版本时，请先更新本文件和 `Directory.Build.props`，再创建对应的 `v*` 标签。CI 与 Release 工作流会检查当前版本是否有日志条目，并在标签构建时检查标签与应用版本是否一致。

This file records user-facing changes by application version. Dates use China Standard Time. Historical entries are based on repository commits and tags. Update this file and `Directory.Build.props` before tagging a release; CI and Release check for a matching entry and version.

## [Unreleased]

## [1.0.17] - 2026-10-07

- 普通图片的 BC1 / DXT1（不透明）与 BC3 / DXT5（渐变透明）新增 Swizzled / Linear 两种；普通 AUTO 和手动排除偏移信息 AUTO 均提供两种排列，默认优先 Swizzled。DDS FourCC 保持 DXT1 / DXT5，真正保留区开头（0x20–0x24）写 GXMSW，未标记仍按线性处理，不增加标记版本。共享 GXM Y-first Morton 块重排，补齐存储但不改逻辑尺寸，更新文件大小估计及中英文说明；已有原生资源和定位 metadata 保持保护，PFS 与散装使用同一规则。需配套引擎识别，不直接修改引擎。
- Added Swizzled/Linear output for ordinary BC1/DXT1 opaque images and BC3/DXT5 alpha images, plus both layout variants for ordinary and manual metadata-excluding AUTO, preferring Swizzled. DDS retains DXT1/DXT5 FourCC and writes GXMSW at reserved offsets 0x20–0x24 without a marker version; unmarked files remain Linear. Shared Y-first Morton block ordering pads storage without changing logical dimensions. Size estimates and bilingual docs are updated; native resources and positioning metadata remain protected, with matching PFS/loose behavior. Requires engine support; no engine edits.
- 新增 PSB DXT5 排列模式，默认 Swizzled（GXM Y-first Morton），可选 Linear；设置持久化且仅 DXT5 可用。以完整 BC3 块重排并补齐 2 的幂存储，逻辑尺寸不变；纯排列互转不重复压缩，支持读取重排后的资源再次缩放。使用独立 `DXT5_SWIZZLED` 标记，提供引擎接入约定，旧引擎需选择 Linear；不直接修改引擎、不影响 PVRTC2 或普通 DDS。
- Added a persistent PSB DXT5 layout selector, defaulting to Swizzled (GXM Y-first Morton) with Linear available. Whole BC3 blocks are reordered into power-of-two-padded storage without changing logical dimensions; layout-only changes are lossless, and swizzled input can be resized. Uses the explicit DXT5_SWIZZLED marker and an engine handoff contract; legacy engines require Linear. PVRTC2 and standalone DDS are unaffected by the PSB layout setting.

## [1.0.16] - 2026-10-07

- PSB 纹理转换新增 PVRTC2 4bpp / 2bpp 选择，默认仍为 DXT5（BC3）；保存格式设置，支持内嵌 RGBA8、DXT5 与两种 PVRTC2 互转，全局 Ratio 和禁用的独立比例功能保持不变。使用随包 PVRTexLib 编码并校验资源长度，重建字符串及资源表，不影响共享字符串的其他引用；附引擎格式接入说明，当前 GXM E-mote PSB 路径需另行支持 PVRTC2。
- Added PVRTC2 4bpp / 2bpp PSB output choices, retaining DXT5 (BC3) as the default and saving the selection. Embedded RGBA8, DXT5 and PVRTC2 can be converted while global Ratio behavior remains unchanged. PVRTexLib encoding validates raw payload lengths; rebuilt string/resource tables preserve unrelated shared-string references. Includes an engine format contract; the current GXM E-mote PSB loader still needs PVRTC2 support.
- 独立缩放未勾选时，渲染补偿也保持未勾选；旧配置中的开启偏好不会显示为有效勾选。
- Render compensation remains unchecked whenever independent resizing is off, including with old enabled preferences.
- PSB 区块整体使用灰色禁用样式，移到页面最底部，并将渲染补偿比例和说明从高级设置移入同一个框。
- Applied a grey disabled style to the entire PSB panel, moved it to the bottom of the page, and grouped render compensation and its explanation inside it.
- 禁用并置灰 PSB 扫描按钮、独立 Ratio 开关及分类比例控件，标题改为灰色；PSB 统一跟随全局 Ratio，关联渲染补偿不可用且不输出配置。保留旧分类参数和 DXT5 压缩功能，自动扫描游戏分辨率不受影响。
- Disabled PSB scanning and independent Ratio controls, with a grey section title. PSBs use the global Ratio; compensation is unavailable and no manifest is emitted. Saved group parameters and DXT5 compression are retained; automatic game-resolution detection is unaffected.

## [1.0.15] - 2026-10-06

- PSB 贴图独立缩放新增默认关闭的勾选项，每次启动不恢复开启状态；分类 Ratio 仍保存。坐标、origin、画布和动作几何改为跟随全局 Ratio。
- Independent PSB resizing always starts unchecked, including with legacy enabled settings; per-resolution ratios remain saved.
- 新增默认勾选的 PSB 渲染补偿，仅独立缩放且处理动画时输出游戏根目录配置；补偿为全局 Ratio ÷ 贴图 Ratio。精简 v2 配置每份 PSB 仅记录整体贴图比例、补偿倍数及资源匹配信息，全局比例只写一次，不输出部件/贴图宽高明细，需配套 GXM 引擎支持。
- Added opt-in independent PSB texture resizing and global-ratio geometry. Optional render compensation (checked by default, active only with independent Animation resizing) exports a compact v2 game-root manifest: one texture ratio, compensation multiplier and resource identity per PSB, one global ratio, and no per-icon/atlas dimensions. Requires a compatible GXM engine.

## [1.0.14] - 2026-10-06

- 修复分辨率/PSB 扫描后的日志更新将主页面拉到底部的问题，保留日志框内部自动滚动；自动扫描分辨率按首个有效 INI 的 WINDOWS 或第一组宽高计算适配 960×540 的 Ratio（不放大）。
- Resolution/PSB scans no longer pull the page down when updating logs; internal log auto-scroll remains. Resolution detection computes a non-upscaling Ratio to fit 960×540 from the first valid INI's WINDOWS section or first dimension pair.
- PSB Ratio 列表在扫描前隐藏，扫描后仅显示实际发现的尺寸；切换目录或重新扫描会清除旧结果，已保存比例独立保留并按尺寸恢复。
- PSB ratios are shown only for discovered dimensions after scanning; changing folders or rescanning replaces visible results while preserving saved ratios.
- 增加 PFS 内及散装 PSB 贴图扫描，按最大图集的完整宽 × 高分类；每类独立配置 Ratio，默认 0.5，并保存设置。
- Added scanning of loose/archived PSB texture dimensions, with persistent independent per-dimension resize ratios defaulting to 0.5.
- PSB 分组实时显示缩放后最大图集的 RGBA8 / DXT5 分配估计及 DXT5 数据大小；软件和中英文 README 说明有损压缩、透明度、GXM 对齐及显存估算边界。
- PSB groups now compare resized largest-atlas RGBA8 / DXT5 allocation estimates with DXT5 data size; bilingual UI/docs explain lossy compression, alpha, GXM alignment and estimate limitations.

## [1.0.13] - 2026-10-05

- 修复 Shift_JIS/CP932 脚本含有无法解码的字节时转换中断的问题：原样保留这些字节，并继续缩放可识别的几何数值。
- 字体削减的字符扫描保留此类脚本中可解码的文本；补充字节保留回归测试与可选的 NUKITASHI 归档验证。
- Preserved undecodable Shift_JIS/CP932 bytes during text scaling instead of aborting conversion.
- Font subsetting now collects readable characters from these scripts; added regression coverage and optional NUKITASHI archive validation.
- 在主界面固定底栏显示游戏资源修改权限与备份提醒；中文、英文界面均可见。
- Added a persistent bilingual reminder about asset rights and backing up the original project.

## [1.0.12] - 2026-09-30

- 修复带编号 PFS 归档的覆盖关系，原生纹理转换时不再丢失或错误覆盖分卷中的资源；补充对应测试。
- Fixed numbered PFS overlay handling during native texture conversion, with regression tests.

## [1.0.11] - 2026-09-30

- 为 PSV 原生纹理增加可手动选择的 `AUTO` 模式，并保留包含位置偏移等元数据的图片；补充适用性检查。
- Added a selectable `AUTO` mode for native textures and preserved images carrying offset/position metadata.

## [1.0.10] - 2026-09-30

- 增加可选的 PSV 原生纹理转换：扫描 PFS 与散装图片、按目录分类，支持 BC/DXT、PVRTC、ETC1 等格式；默认关闭总开关。
- 增加扫描并列出根目录及 PFS 内所有 `system.ini` 分辨率的 UI，可选择条目预览，不改变 Ratio。
- 改善界面滚动流畅度，并更新四张界面截图。
- Added opt-in native PSV texture conversion, a selectable INI-resolution list, and smoother UI scrolling.

## [1.0.9] - 2026-09-29

- 增加默认开启、可单独关闭的 E-mote PSB 内嵌 RGBA8 图集转 DXT5/BC3 功能。
- 修复文本缩放误改脚本算术、控制值、注释和台词的问题；仅缩放可确定的几何数值，并补充回归测试。
- Added optional PSB atlas conversion to BC3 and corrected text-scaling false matches.

## [1.0.8] - 2026-09-22

- 四个平台分别提供带 FFmpeg 与不带 FFmpeg 的版本化发布包；补充自行放置 `ffmpeg`/`ffprobe` 的说明。
- Added versioned `with-ffmpeg` and `no-ffmpeg` packages for all four platforms.

## [1.0.7] - 2026-09-20

- 加固视频/动画转码，发布包改用 FFmpeg Full 构建并检查所需编解码能力。
- 修复发布包遗漏 `ffprobe` 的问题。
- Hardened media conversion and FFmpeg Full packaging; included the missing `ffprobe` binary.

## [1.0.6] - 2026-09-20

- 支持 Artemis/E-mote 动态立绘 PSB 的图集与相关几何数据缩放，兼容 PFS 内外的 PSB。
- 扩展 OTF 字体削减及视频处理测试。
- Added E-mote PSB conversion, OTF font subsetting support, and additional media tests.

## [1.0.5] - 2026-09-15

- 将 PFS 外的散装视频转换为 H.264/AAC MP4，而非只处理 `.dat`。
- Converted loose videos outside PFS to H.264/AAC MP4.
- 注：仓库有 `1.0.5` 版本提交，但没有 `v1.0.5` Git 标签。

## [1.0.4] - 2026-09-14

- 保留非视频 `.dat` 与 PFS 内需原样保留的 `.dat`；转换失败时日志显示具体资源文件。
- Preserved non-video/archived DAT data and added the failed asset path to conversion logs.
- 注：仓库有 `1.0.4` 版本提交，但没有 `v1.0.4` Git 标签。

## [1.0.3] - 2026-09-13

- 对齐 Artemis 文本与音视频转换的 PSV 参考结果，增加相关测试。
- 修复发布工作流，锁定 x264 来源，并移除不稳定的截图快照 CI 任务。
- Aligned text/media conversion with PSV reference output and repaired release automation.

## [1.0.2] - 2026-09-13

- 图片缩小时保留 PNG 原存储方式、颜色类型、索引调色板及透明度信息。
- Preserved PNG storage, palette, color type, and transparency during resizing.

## [1.0.1] - 2026-09-13

- 修复已从 PFS 解包的资源未按勾选项转换的问题，增加归档内资源回归测试。
- Fixed processing of resources extracted from PFS archives.

## [1.0.0] - 2026-09-13

- 首个跨平台 Avalonia/.NET NativeAOT 版本：独立 PFS 解包与重打包、资源转换、中文/英文界面、图标、许可证与 GitHub Actions。
- 初期 CI 与 FFmpeg 随包分发问题在该标签前得到修复。
- Initial cross-platform application with PFS conversion, bilingual UI, icon, licensing, and automated builds.
