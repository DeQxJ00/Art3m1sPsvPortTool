# PSV 会话功能需求：E-mote Psb渲染比例补偿

本文件交给 PSV/GXM 会话实施。本工具会话只负责转换、配置输出及工具 UI，不直接修改 `art3m1s-psv-gxm`。此前引擎试验改动已逐项撤回，保留引擎会话原有修改。旧解析器备份在本工具忽略目录 `artifacts/gxm-render-reference/`，属于已作废的版本 1，不能按其配置接口实施，更不得覆盖引擎当前代码。

## 引擎选项

- 中文名称严格使用：`E-mote Psb渲染比例补偿`。
- 默认开启，保存为每游戏独立偏好。
- 当前游戏根目录没有对应 `art3m1s_psb_render.json` 时，选项灰色、不可操作，补偿实际不生效；不要把保存的开启偏好改成关闭。
- 配置损坏、版本不支持或没有有效模型条目时同样置灰，说明原因并记录诊断。换游戏后重新判断，不能沿用上一游戏的配置或可用状态。
- 有有效配置时允许关闭；关闭保持原有渲染，不修改资源文件。
- 切换状态要正确失效/区分解析模型、预解析、姿态和顶点缓存；若必须重启游戏才生效，明确提示，不能静默延后。

建议界面说明，同时提供英文译文：

> 读取游戏根目录的 art3m1s_psb_render.json，补偿独立缩小贴图后的 E-mote PSB 部件显示尺寸。补偿倍数 = 全局 Ratio ÷ 贴图 Ratio；例如 0.5 ÷ 0.25 = 2×。仅补偿部件宽高与网格域，不再次放大人物坐标、画布或 UV。默认开启；没有有效配置时不可用。关闭后使用原有渲染，独立缩放的立绘可能出现大小不匹配。

## 工具行为与配置接口（已实现）

- 独立缩放默认关闭：PSB 贴图、坐标和画布全部跟随全局 Ratio，不输出补偿配置。
- 开启后，分类贴图 Ratio 默认 0.5，仅作用于图集、裁切尺寸和 icon 物理采样矩形；origin、screenSize、动作坐标/偏移/路径/bounds/空白网格域跟随全局 Ratio。
- 工具的 PSB 渲染补偿默认勾选，仅独立缩放与动画处理同时启用才输出配置。不支持或原样保留的 PSB 不生成条目。
- 配置放在输出游戏根目录、与 PFS 并列；PFS 内与散装 PSB 同样适用。配置与资源一起暂存，成功后原子提交输出。

完整示例见 [PSB 渲染配置](psb-render-config.md)，C# 定义在 `src/Art3m1s.PsvTool.Core/PsbRenderManifest.cs`。

- `version`: 2（替代未发布的逐部件版本 1）。
- 根 `geometryRatio`: 全局 Ratio，全文件只写一次，仅说明和校验；PSB 几何已转换，不再次缩放。
- `models[].path`: PSB 虚拟路径，标准 `/` 分隔，读取时兼容 `\`。
- `archive`: 诊断用 PFS 文件名，散装为 null；匹配实际资源需使用 path 和 fingerprint，不能依赖归档顺序。
- `fingerprint`: 输出 PSB 全部字节的 FNV-1a 64-bit，小写 16 位十六进制；初值 14695981039346656037，每字节 `(hash XOR byte) * 1099511628211`，u64 回绕。仅防错配，不是安全签名。
- `models[].textureRatio`: 整份 PSB 的贴图比例，所有图集共用，按面积最大的图集所属分类选取。
- `models[].renderScale`: 全局 geometryRatio / 该 PSB textureRatio。
- 每份 PSB 只写一条整体比例记录，没有 `icons[]`、贴图名称或任何宽高明细。配置大小不随图集/部件数量增长。

引擎自行读取 PSB 元数据，逻辑部件宽高 = PSB 中物理采样宽高 × renderScale，不能再次乘比例商。整数取整和极小矩形的 1 像素保底会有像素级误差，统一倍数不保证精确恢复原宽高；不要为此重新扩展逐部件配置。

## 实施注意

不要直接放大玩家/人物外层 affine：动作坐标和 origin 已按全局 Ratio 缩放。应分离逻辑绘制宽高与物理 UV 矩形，覆盖普通 quad、网格域/变形、遮罩和可选 Eluna；纹理上传尺寸和像素不变。

相关入口：`core/src/runtime/emote_source_cache.rs`（解析/预解析缓存），`core/crates/art3m1s-emote/src/render.rs`（mesh 域），`core/src/runtime/emote.rs`（quad_size、uv_scale、native material uv_rect），以及 Eluna 的 `emote.rs`、`sdk.rs`、`core/src/runtime/emote/eluna.rs`。Eluna 的 resolved 宽高目前同时用于绘制与 UV，需要拆开。

通过现有游戏 VFS 读配置。验证大小、条目数、有限正比例与重复条目；缺失/无效/未知版本/指纹错配时安全退回旧行为并记录原因，不阻断整个游戏。当前接口限制 8 MiB、10,000 模型；geometryRatio/textureRatio 为 (0,1]，renderScale 为有限正数，计算绘制尺寸也须防止溢出。不执行配置中的路径指令，不写回资源。

## 验收

1. 默认开启；无配置/无效配置置灰；换游戏重新判断，偏好保留。
2. 开关有效；缓存、预加载和重载不会混用不同状态的几何。
3. 全局 0.5，贴图 0.25/0.5/1：整数可整除的合成样例补偿开启时绘制大小、origin、最终位置一致，UV 正确；非整除尺寸检查符合统一倍率及取整预期，不宣称精确恢复。关闭使用旧行为。
4. 多图集、矩形尺寸、非整数及极小采样矩形；普通 quad、网格、遮罩和可选 Eluna。
5. PFS/散装、同名覆盖、错配指纹、损坏配置、换游戏和资源释放。
6. 中英文说明及 PSV 实机验证；不能仅凭 host 测试声称实机通过。

工具已有原创合成模型，不含商业游戏素材。可在工具仓库导出供引擎测试：

```powershell
$env:ART3M1S_PORT_TEST_DIR = 'F:/WorkSpaceAI2/art3m1s_psv_port_tool/artifacts/psb-render-fixtures'
dotnet test tests/Art3m1s.PsvTool.Core.Tests -c Release --filter FullyQualifiedName~PsbRenderTests
```

请在 PSV 会话实施、验证并报告结果。本次需求交接不授权自动改版本号、commit、tag 或 push。
