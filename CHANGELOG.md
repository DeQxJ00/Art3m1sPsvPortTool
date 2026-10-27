# 更新日志 / Changelog

本文件按应用版本记录用户可见的功能和重要修复。日期采用北京时间；历史内容依据仓库提交与标签整理。发布新版本时，请先更新本文件和 `Directory.Build.props`，再创建对应的 `v*` 标签。CI 与 Release 工作流会检查当前版本是否有日志条目，并在标签构建时检查标签与应用版本是否一致。

This file records user-facing changes by application version. Dates use China Standard Time. Historical entries are based on repository commits and tags. Update this file and `Directory.Build.props` before tagging a release; CI and Release check for a matching entry and version.

## [Unreleased]

## [1.0.28] - 2026-10-27

- PSV 图片纹理区域新增分类方式下拉框，默认自动识别，可选手动 1–8 层并保存设置。自动跳过已知资源、分辨率、平台和语言包装层，识别 BG/FG/CG 等目录；UI/遮罩保护优先，不明确的父目录保守回退。切换清空旧分类并提示重新扫描；PFS 内外、实际转换及 CLI `--texture-category-depth auto|1..8` 使用相同规则，不改变路径或全局 Ratio。
- Added a persisted image-classification selector: automatic by default, or manual depths 1–8. Automatic mode skips known resource/resolution/platform/language wrappers to detect BG/FG/CG roles; UI/mask protection takes precedence and unknown parents fall back conservatively. Changing modes clears stale categories and requires rescanning. Archived/loose scans, conversion and CLI `--texture-category-depth auto|1..8` share the same rules without changing paths or the global Ratio.

- CSV 坐标转换先检查同目录、同名的非 CSV 文件或文件夹（包含 Lua），不再仅凭固定资源目录或命名匹配；字体布局表继续单独识别。无配套资源、未知字段及动态表达式原样保留，并补充归档 / 散装、大小写及文件夹配对测试。
- CSV coordinate conversion now requires a sibling non-CSV file or folder with the same basename (including Lua), instead of relying on resource-tree or name patterns alone. Font-layout tables remain separately recognized. Unpaired resources, unknown fields and dynamic expressions stay unchanged, with archived/loose, case-insensitive and folder-pairing regressions.

## [1.0.27] - 2026-10-25

- 补齐 IET 中已知引擎几何指令的坐标、锚点及宽高缩放，保留时间、延迟、缩放倍率、注释、对话和 Lua 区块；动态表达式与未知宏保持原样。归档与散装 IET 均随文本转换选项处理。
- Added coordinate, anchor and dimension scaling for known IET engine geometry commands while preserving timing, delays, zoom factors, comments, dialogue and Lua blocks. Dynamic expressions and unknown macros remain unchanged; archived and loose IET files follow the text conversion option.
- 补齐图片配套 CSV 的坐标、边距和字体布局缩放，归档与散装资源均随文本转换选项处理；保留行列数、字数上限、比例、颜色和时间等非像素数据。未知 CSV 保持原样。
- Added image-sidecar CSV coordinate, margin and font-layout scaling for archived and loose resources under the text conversion option. Counts, character limits, percentages, colors and timing remain unchanged; unknown CSV schemas are preserved.

## [1.0.26] - 2026-10-24

- 修复 PNG 扫描和缩放遇到可选 `iCCP` 色彩配置 CRC 错误时中断转换的问题：仅从解码输入和输出中移除损坏的配置块，保留有效配置，日志以 `PNG_ICCP_CRC_IGNORED` 警告记录资源路径及字节偏移；不修改原始资源。像素、调色板、透明度及其他数据块的 CRC 校验仍严格执行，Gray8、索引位深、`PLTE` / `tRNS` 和 IEND 后附加数据继续保留。
- Fixed PNG scanning/resizing aborting on invalid CRCs in optional `iCCP` color profiles. Only corrupt profiles are discarded from decoder input/output; valid profiles remain intact. `PNG_ICCP_CRC_IGNORED` warnings identify resource paths and byte offsets without modifying source assets. Pixel, palette, transparency and other chunk CRC checks remain strict, preserving Gray8, indexed depths, `PLTE` / `tRNS` and post-IEND data.
- 增加损坏 / 有效配置、其他 CRC 错误、结构错误、颜色存储类型和归档 / 散装转换回归测试，并以仓库外本地样本验证 NativeAOT 命令行输出。
- Added regressions for corrupt/valid profiles, other CRC failures, structural errors, storage formats and archived/loose conversions, with NativeAOT CLI output verified against external local fixtures.

## [1.0.25] - 2026-10-23

- 补齐 CLI 路径预检中的 macOS 系统别名支持，并统一输入 / 输出重叠、日志保护目录的别名比较，避免初始化失败及后续测试超时；自建链接和悬空链接仍拒绝。发布许可收集改为随源码保存并复制完整 PowerVR Tools EULA，避免上游网页 HTTP 401 阻断打包，其他许可下载失败会记录具体文件及 URL。
- Completed macOS system-alias handling in CLI path preflight and overlap/protected-log checks, preventing setup failures and cascading timeouts while retaining custom/dangling-link rejection. Packages now copy the complete reviewed PowerVR Tools EULA stored in source instead of depending on an upstream page returning HTTP 401; other license download failures identify the file and URL.
- 无法识别为视频的 DAT 原样保留属于正常回退，探测诊断和保留原因改记完整日志 INFO，不再虚增错误 / 警告数；非 DAT 视频探测及实际转码失败仍记录 ERROR 并报错。不支持的 PSB 格式及超过 4096 的长图警告继续保留。
- Unrecognized DAT preservation is now logged as INFO, including probe diagnostics, rather than inflating warning/error counts. Non-DAT probe and actual encoding failures remain errors. Unsupported PSB formats and oversized-texture warnings are retained.

- 修复 macOS 日志路径校验误拒绝系统 `/var`、`/tmp`、`/etc` 别名而导致 CI / Release 多项测试连带失败的问题；仅放行解析目标与标准 `/private/...` 对应路径一致的系统链接，并以一致的别名路径检查日志不能写入输入 / 输出目录。用户自建链接、junction 及悬空链接仍拒绝。新增路径与安全回归测试，CI / Release 无论测试成功与否均保留 TRX 结果用于诊断。
- Fixed macOS log validation rejecting standard `/var`, `/tmp` and `/etc` aliases and cascading into CI/Release test failures. Only system links resolving to their exact `/private/...` targets are allowed, with consistent alias-aware protected-directory checks. Custom links, junctions and dangling links remain forbidden. Added path/security regressions and always-uploaded CI/Release TRX results.
- 新增 PSB ASTC 输入转换：`ASTC_8BPP`（4×4）及明确块尺寸的标准 2D ASTC 经 PVRTexLib 解码、按全局 Ratio Bicubic 缩放后，输出所选 DXT5 Linear / Swizzled、PVRTC2 4bpp / 2bpp；同步几何、资源表及大小估算。关闭格式转换时保留 ASTC 类型并按 Ratio 重编码。日志记录图集、源 / 目标格式与尺寸，损坏长度明确报错；含糊或未知的 ASTC 格式仍警告保留，不猜测块尺寸。
- Added PSB ASTC inputs: `ASTC_8BPP` (4x4) and explicit standard 2D footprints decode through PVRTexLib, resize by the global Ratio with Bicubic and encode to selected DXT5 Linear/Swizzled or PVRTC2 4bpp/2bpp. Geometry, resource tables and size estimates stay in sync. Disabling format conversion retains/re-encodes ASTC on resize. Logs include atlas, formats and dimensions; malformed lengths fail explicitly, while ambiguous/unknown ASTC types remain preserved with warnings.
- PNG 扫描与缩放统一使用零拷贝的结构解析层，像素解码与缩放继续由 ImageSharp 负责；统一校验长度、CRC、必要块顺序、颜色类型和位深，错误包含块名与字节偏移。覆盖所有合法颜色 / 位深组合及 Adam7、连续 IDAT、未知附加块和损坏输入；APNG 暂不支持缩放，明确报错而不误写动画帧。
- PNG scanning and resizing now share a zero-copy container parser, with pixel decoding/resampling still handled by ImageSharp. Lengths, CRCs, required chunk ordering and color/depth combinations are validated consistently, with chunk names and byte offsets in errors. Tests cover legal color/depth combinations, Adam7, consecutive IDATs, unknown ancillary chunks and malformed input. APNG resizing explicitly fails instead of rewriting stale animation data.
- 修复合法 PNG 在 IEND 后带额外数据时，图片分类扫描误报数据块截断并中止的问题。扫描在 IEND 停止，附加数据作为不透明尾部逐字节保留；RGB、RGBA、Gray8 及索引色缩放均保留尾部，且不将带尾部的 PNG 转为会丢失附加数据的原生纹理。
- Fixed category scans reporting truncated chunks for valid PNGs carrying bytes after IEND. Scanning stops at IEND and preserves the opaque trailer byte-for-byte through RGB/RGBA/Gray8/indexed resizing; trailer-bearing PNGs stay out of native conversion to avoid losing attached data.
- UI 和命令行共用扫描图片后的分类与建议勾选规则；命令行等待分辨率、图片分类扫描及规则生成全部完成才开始转换，记录各分类勾选 / 格式及扫描完成标记。图片扫描失败或取消时不进入转换，新增异步顺序和 UI / CLI 分类一致性测试。
- GUI and CLI now share scanned-image category/recommendation construction. CLI waits for resolution detection, image scanning and rule generation before conversion, logging category selections/formats and scan completion. Failed or cancelled image scans never start conversion; added async-ordering and GUI/CLI category parity tests.
- 命令行默认执行 UI“自动扫描分辨率”同一套选择与适配逻辑：首个有效 INI 优先 WINDOWS，否则首组，图片检测兜底；自动计算不放大且适配 960×540 的 Ratio，不再默认固定 0.5。只有显式数值参数才固定 Ratio；新增 UI / CLI 一致性及 720p 实际 EXE 转换测试。
- CLI defaults now reuse the GUI's automatic-resolution selection and fit calculation: prefer WINDOWS in the first usable INI, otherwise its first entry, with image detection as fallback. Ratio fits within 960x540 without upscaling instead of defaulting to fixed 0.5; explicit numeric overrides remain supported. Added GUI/CLI parity and real-executable 720p conversion coverage.
- 命令行转换结束后自动清理本任务的 `.conversion.lock`，覆盖成功、失败、取消、跳过和初始化失败；采用独占句柄关闭删除，未取得锁的任务不会清理，避免误删其他正在转换任务的锁。补充锁竞争、不同输出互不干扰及旧锁接管的回归测试。
- CLI conversions now clean their owned `.conversion.lock` on success, failure, cancellation, skipping and setup failure. Exclusive delete-on-close handles avoid post-release path deletion; unsuccessful contenders never remove another task's lock. Added contention, independent-output and stale-sidecar regression coverage.

## [1.0.24] - 2026-10-22

- 程序每次转换默认创建独立的 `游戏文件夹名.errors.log`，无需 BAT 重定向；仅保存运行边界、结果、警告及详细错误，普通进度保留在完整日志。正常完成时也记录零错误状态，预览仍不写文件。
- The application now creates a separate `GAME.errors.log` for every conversion without BAT redirection, retaining run boundaries, outcome, warnings and detailed errors but excluding ordinary progress. Successful runs record zero-error status; previews still write no files.
- 批处理 / CLI 默认自动保存日志，文件名简化为 `游戏文件夹名.log` 和 `游戏文件夹名.errors.log`；重复运行追加，按记录中的运行编号区分，无需指定 `--log`。
- Batch/CLI logs are saved automatically as `GAME.log` and `GAME.errors.log`; repeated runs append with per-record run IDs, without requiring `--log`.
- 完善 UI / CLI 日志：按游戏目录名及运行编号保存完整日志，警告 / 错误单独存档；不再因控制台节流丢弃资源记录。补全阶段、PFS、资源路径、异常链、FFmpeg 命令 / stderr / 重试、字体字节变化及初始化错误回退；图片与 PSB 图集超过 4096 时明确记录源 / 目标尺寸。
- Improved GUI/CLI logs grouped by game and run ID, with separate warnings/errors and unthrottled file records. Added stage/archive/resource context, exception chains, FFmpeg commands/stderr/retries, font byte changes, setup-error fallback and explicit original/target dimensions for images/PSB atlases exceeding 4096.
- UI 与 CLI 共用转换默认值，新增可省略的 `--preset ui-defaults`：Ratio 默认 0.5、字体削减默认关闭，自动适配需显式 `--ratio auto`。默认配置验收只开启字体削减并选择简体中文，其余保持默认，校验参数一致性、纹理排列与实际字体输出。
- UI and CLI now share conversion defaults through optional `--preset ui-defaults`: Ratio 0.5, font subsetting off; automatic fitting requires `--ratio auto`. Default-configuration acceptance enables only Simplified Chinese font subsetting, checking option parity, texture layout and actual font output.
- 完善单游戏命令行：`--convert` 的输入为直接包含 PFS 的游戏根目录，输出为指定的最终目录；支持 Ratio、处理类型、纹理 / 字体、日志、预览、取消及覆盖策略。`--batch-convert` 为同义别名，不再遍历父目录或维护待办清单；中英文 README 增加逐游戏调用及 CMD / PowerShell 批量脚本示例。
- Expanded single-game CLI: `--convert` takes the game root containing PFS and the exact output directory, with Ratio, resource/texture/font options, logs, previews, cancellation and overwrite policies. `--batch-convert` is an alias with no parent-directory traversal or checklist. Bilingual READMEs show per-game usage and CMD/PowerShell scripts for repeated calls.
- “图片压制为 PSV 专用纹理”首次启动默认勾选；配置缺失、损坏或未包含该设置时采用开启默认值，已有明确选择继续恢复，手动关闭状态仍可保存。扫描不改变总开关。
- Native PSV image texture conversion now starts enabled on first launch, or when configuration is missing, invalid or omits this setting. Explicit saved choices, including disabled, still persist; scanning leaves the master switch unchanged.

## [1.0.23] - 2026-10-21

- 文本坐标中的尾随全角数字不参与数值计算：只缩放前面的半角数值，例如 Ratio 0.5 时 `960４` → `480４`、`-640３` → `-320３`；全角后缀逐字保留，纯全角数字不转换，不因此中断流程。坐标串、裸数值、INI 和相关元组使用同一原则，记录保留内容所在行号；INI 平台统一不会二次覆盖此类字段。
- Trailing fullwidth digits in text coordinates are excluded from arithmetic: only the ASCII numeric prefix scales, e.g. at Ratio 0.5, `960４` → `480４` and `-640３` → `-320３`. Suffixes stay unchanged; entirely fullwidth values are preserved without aborting conversion. Applies to coordinate strings, bare values, INI fields and relevant tuples, with preserved-content line numbers reported; platform INI unification does not overwrite these fields afterward.
- 移除大图扫描的像素数与编码大小限制，使长背景等资源仍能按 Ratio 缩放；图片检查失败时停止发布输出并报告错误，避免静默保留未适配的原图。补充大图缩放和失败输出清理测试。
- Removed pixel-count and encoded-size image scan cutoffs so large scrolling backgrounds can still resize by Ratio. Image inspection errors now stop output publication and are reported instead of silently retaining unadapted originals. Added oversized-image resizing and failed-output cleanup coverage.

## [1.0.22] - 2026-10-19

- 修复交错索引色 PNG 缩放后仍保留 Adam7 标记、导致像素数据不足和解码失败的问题。输出普通扫描行时同步写入非交错标记，继续保留调色板、透明度和目标尺寸；新增 1/2/4/8 位交错 PNG 回归测试。
- Fixed indexed PNG resizing retaining an Adam7 header while emitting ordinary scanlines, causing incomplete-image decode errors. Output now declares non-interlaced data while preserving the palette, transparency and target dimensions. Added regression tests for 1/2/4/8-bit interlaced PNGs.

## [1.0.21] - 2026-10-19

- 启用文本处理时，将 `system.ini` 中 WINDOWS、ANDROID、IOS、WASM、SWITCH、PS4 和 VITA 平台节的宽高统一为转换目标尺寸，补齐缺失的 WIDTH/HEIGHT；目标来自界面选定分辨率乘以 Ratio，未指定时使用扫描结果。保留其他节、注释、编码及换行，不改变脚本坐标自身的缩放规则；归档与散装 INI 使用相同行为。
- When text processing is enabled, `system.ini` sections for WINDOWS, ANDROID, IOS, WASM, SWITCH, PS4 and VITA now share the conversion target dimensions, adding missing WIDTH/HEIGHT fields. Targets come from the UI-selected resolution times Ratio, falling back to scanned dimensions when omitted. Other sections, comments, encoding and line endings are retained; script coordinates keep their existing scaling rules. Applies to both archived and loose INI files.
- 增加目标宽高成对校验，以及平台画布统一、禁用文本处理、原文件不变、编码与重复处理的回归测试。
- Added validation of paired target dimensions and regression coverage for platform canvases, disabled text processing, unchanged inputs, encoding and repeated processing.

## [1.0.20] - 2026-10-19

- 扫描图片分类后，自动勾选 AUTO 识别到可安全转换的 BG/FG/CG 等分类，默认使用 Swizzled；继续保护灰度、UI、偏移元数据和不适合转换的图片，不自动打开总开关，重新扫描保留已有分类设置。
- Image scanning now automatically selects categories with suitable AUTO-detected images (BG/FG/CG, etc.), using Swizzled by default. Grayscale, UI, offset metadata and unsuitable images remain protected; scanning leaves the master switch unchanged and preserves existing category settings on rescans.
- 图片纹理转换标题改为“建议转换”，中英文同步；仅修改提示，实际默认开关状态不变。更新双语说明，补充分类识别、保护规则、PFS/散装扫描及重新扫描的回归测试。
- Updated the bilingual texture conversion title to “recommended” without changing the default master-switch state. Refreshed documentation and added coverage for category detection, preservation rules, archived/loose scanning and rescans.

## [1.0.19] - 2026-10-17

- 修复 PSB v4 空扩展资源表与普通资源表共用地址时，转换重建数据区越界的问题；保留空表并正确迁移指针，增加重叠范围校验，补充共享空表、格式转换与重复缩放的合成回归测试。
- Fixed PSB v4 conversion failures when empty extra-resource pointers alias regular resource tables. Empty tables are preserved and their pointers relocated without rebuilding nonexistent payloads; added overlap validation and synthetic regression coverage for shared empty tables, format conversion and repeated resizing.
- AST 坐标转换补充 `basex` / `basey`，基准偏移跟随目标画布缩放；保留缩放倍率和图层编号，补充正负偏移测试。
- AST coordinate conversion now scales `basex` / `basey` with the target canvas while preserving zoom factors and layer IDs, with signed-offset regression tests.
- 暂时保留 ImageSharp 3.1.12，NU1902 / NU1903 漏洞审计仍显示警告，但不再因警告视为错误而阻断 CI 和发布还原；其他警告仍按错误处理。此调整不修复漏洞、不关闭审计；已修复的 4.x 版本需要先配置官方许可 key。
- Temporarily retained ImageSharp 3.1.12 and kept NU1902 / NU1903 audit warnings visible without failing CI/release restore. Other warnings remain errors. This does not fix vulnerabilities or disable auditing; upgrading to the patched 4.x release requires an official license key first.

## [1.0.18] - 2026-10-08

- PSB 纹理格式旁新增异步“计算大小”，按全局 Ratio、DXT5 排列和 PVRTC2 格式统计 PFS 内外全部图集的原/目标数据大小，按宽 × 高列明细；共享资源去重，异常及不支持资源明确排除。扫描后选项变更即时重算，不修改文件、不启用独立缩放、不改变主页面滚动位置；补充实际编码长度与 UI 验证。
- Added asynchronous PSB texture size estimates beside the format selector, accounting for global Ratio, DXT5 layout and PVRTC2 blocks across archived/loose atlases. Groups by width × height, deduplicates shared resources, excludes errors explicitly, and updates immediately when options change. Metadata-only, without enabling independent scaling or moving the viewport; verified against encoded payloads and UI checks.

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
- 字体削减的字符扫描保留此类脚本中可解码的文本；补充字节保留回归测试与可选的 Case004 归档验证。
- Preserved undecodable Shift_JIS/CP932 bytes during text scaling instead of aborting conversion.
- Font subsetting now collects readable characters from these scripts; added regression coverage and optional Case004 archive validation.
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
