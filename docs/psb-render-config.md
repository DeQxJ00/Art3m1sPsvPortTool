# PSB 渲染配置 / PSB render configuration

> 当前软件已禁用独立 Ratio 和补偿控件（置灰），PSB 统一跟随全局 Ratio，不输出此配置。以下为原接口参考，不代表当前可启用的功能。
>
> The current app disables independent Ratio and compensation controls. PSBs follow the global Ratio and this manifest is not emitted. The following documents the previous interface for reference, not an available feature.

启用 **PSB 贴图分辨率单独缩放**（默认关闭）、**PSB 渲染补偿**（默认勾选）和动画处理后，成功处理的 E-mote PSB 会记录在输出游戏根目录的 `art3m1s_psb_render.json`。配置与 PFS 一起复制，不放在工具程序目录，也不需要塞入 PFS。散装 PSB 和 PFS 内 PSB 使用同一规则。

With independent PSB resizing (off by default), render compensation (checked by default) and Animation processing enabled, successfully processed E-mote PSBs are recorded in `art3m1s_psb_render.json` at the output game root. Copy it alongside the PFS files, not alongside the conversion executable. Loose and archived PSBs use the same rules.

## 比例 / Ratios

| 全局 / Global | PSB 贴图 / PSB texture | 补偿 / Compensation |
| --- | --- | --- |
| 0.5 | 0.25 | 2× |
| 0.5 | 0.5 | 1× |
| 0.5 | 1 | 0.5× |

- 整份 PSB 的所有图集共用一个贴图 Ratio，按面积最大的图集所属分辨率分类选取。
- 图集尺寸、裁切尺寸、icon left/top/width/height 是物理像素，跟随贴图 Ratio。
- origin、screenSize、动作坐标、偏移、路径、bounds 和空白网格域跟随全局 Ratio。
- 配置每个 PSB 只保存一条整体比例记录，不列贴图、部件或宽高；全局 Ratio 只写一次。配置大小随 PSB 数量增长，不随部件数量增长。
- 引擎侧由 PSV 会话独立实施：使用整体补偿倍数调整逻辑部件宽高，UV 仍使用物理矩形；普通部件、网格与可选 Eluna 都应区分两类尺寸，不放大玩家外层变换。需求见 [PSV 功能交接](gxm-emote-compensation-request.md)。

All atlases in a PSB share one texture Ratio selected from its largest-area atlas's resolution group. Atlas/truncation dimensions and icon sampling rectangles follow that Ratio. Origins, canvas, motion coordinates, offsets, paths, bounds and blank mesh domains follow the global Ratio. Each PSB emits one flat ratio record: no atlas/icon names or dimensions. The global Ratio is stored once. Configuration size grows with model count, not part count. The separate PSV thread implements compensation of logical part sizes while preserving physical UVs and the outer player transform, including sprites, meshes and optional Eluna. See the [engine feature handoff](gxm-emote-compensation-request.md).

## 字段 / Schema

版本 2 替代未发布的逐部件版本 1。以下仅为字段示例；fingerprint 必须匹配实际输出 PSB，不可直接拿示例套用。

Version 2 replaces the unreleased per-icon version 1. Field example only: `fingerprint` must match the actual converted PSB.

```json
{
  "version": 2,
  "geometryRatio": 0.5,
  "models": [
    {
      "path": "image/fg/hero.psb",
      "archive": "root.pfs.010",
      "fingerprint": "0000000000000000",
      "textureRatio": 0.25,
      "renderScale": 2
    }
  ]
}
```

- 根 `geometryRatio`：全局 Ratio，仅用于说明、校验。几何已写入 PSB，引擎不能据此再次缩放坐标或画布。
- `path`：游戏虚拟路径；`archive` 仅用于诊断，散装资源为 null。同路径资源通过输出 PSB 字节指纹区分，不依赖 PFS 覆盖顺序。
- `fingerprint`：输出 PSB 的 FNV-1a 64-bit 小写十六进制，仅防止错配，不是安全签名。
- `textureRatio`：整份 PSB 的贴图比例，不按图集重复保存。
- `renderScale`：全局 Ratio ÷ 贴图 Ratio；引擎以 PSB 自身的物理部件宽高乘此倍数得到逻辑绘制宽高，不改变坐标、origin、画布或 UV。不额外再乘一次比例商。

Root `geometryRatio` records the global Ratio for explanation/validation only; geometry has already been converted. `path` is the virtual resource path; `archive` is diagnostic only (null for loose files). The output-byte FNV-1a fingerprint disambiguates overlays; it is not a security signature. `textureRatio` applies to the whole PSB. Multiply physical part dimensions from the PSB by `renderScale` to obtain logical dimensions. Do not scale positions, origins, canvas or UVs, or apply the ratio quotient again.

整数采样尺寸经过取整，统一倍数可能产生像素级尺寸偏差，极小部件还会保底为 1 像素；本接口不保证精确恢复逐部件原宽高。不增加宽高明细来消除这类误差。

Integer sampling dimensions are rounded and very small rectangles are clamped to at least one pixel. Uniform compensation may introduce pixel-level size differences; it does not reconstruct exact original per-part dimensions. No dimension lists are emitted to correct this rounding.

引擎功能名称为 `E-mote Psb渲染比例补偿`，默认开启；没有有效对应配置时选项置灰，实际保持旧渲染行为，不改变保存的开启偏好。格式错误、未知版本或指纹不匹配应忽略并记录诊断。换游戏/重新初始化时重新读取，不能沿用上一游戏的配置。上限为 8 MiB、10,000 个模型。旧引擎不支持此配置，应关闭独立缩放；本工具拒绝将带此配置的输出再次作为输入。

The engine option is enabled by default and disabled in the UI when no valid matching configuration exists, preserving its saved preference and legacy rendering. Invalid/unknown versions or fingerprint mismatches must be ignored with diagnostics. Reload configuration for each game. Limits are 8 MiB and 10,000 models. Older engines require independent resizing to be off. Re-converting an output containing this manifest is rejected.

## 验证 / Verification

工具单元测试覆盖开关组合、两种比例、取整、多图集、散装与归档、最终 PSB 指纹，以及精简 JSON 字段与大小。下面可导出原创合成模型供 PSV 会话建立引擎测试；此前试验用引擎修改和测试已经撤回。实机验证仍待引擎会话完成。

Port-tool tests cover toggles, both ratios, rounding, multiple atlases, loose/archived files, final byte fingerprints and compact JSON fields/size. Export synthetic fixtures for the PSV thread to implement engine tests. Earlier prototype engine changes/tests have been withdrawn. Vita hardware verification remains pending.

```powershell
$env:ART3M1S_PORT_TEST_DIR = 'F:/WorkSpaceAI2/art3m1s_psv_port_tool/artifacts/psb-render-fixtures'
dotnet test tests/Art3m1s.PsvTool.Core.Tests -c Release --filter FullyQualifiedName~PsbRenderTests
```
