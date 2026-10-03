# 更新日志 / Changelog

本文件按应用版本记录用户可见的功能和重要修复。日期采用北京时间；历史内容依据仓库提交与标签整理。发布新版本时，请先更新本文件和 `Directory.Build.props`，再创建对应的 `v*` 标签。CI 与 Release 工作流会检查当前版本是否有日志条目，并在标签构建时检查标签与应用版本是否一致。

This file records user-facing changes by application version. Dates use China Standard Time. Historical entries are based on repository commits and tags. Update this file and `Directory.Build.props` before tagging a release; CI and Release check for a matching entry and version.

## 未发布 / Unreleased

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
