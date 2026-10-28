# 上游 10.20.08 与当前 fork 差异分析

> 分析对象：`GaoXanSheng/JiaolongControl` tag `10.20.08`
> 对比对象：本 fork `remake/jiaolong-control` @ `04811f6`（AppVersion `10.19.74`）
> 分析日期：2026-10-28
> 结论口径：以「上游真实增量」与「fork 真实增量」做三方比对，而非简单 tree diff

---

## 0. 结论先行

1. **上游 10.20.08 相对本 fork 的基线（10.19.32）只有 6 个提交、73 个文件、+1565 / −1164 行**，不是 195 个提交的大版本。
2. **功能增量很小**：1 个交互组件（点击数字直接输入）、1 个新功能（SMU 开机勾选项）、1 次首页/窗口布局自适应改造、1 次 vite 安全升级。
3. **真正的价值集中在"修 bug"**：WMI CPU 电压解析、SMU 的 TDC/EDC 遥测取错传感器、GPU 风扇转速单位、AutoFan 停止返回值等——其中至少 3 项**本 fork 目前仍然中招**。
4. **但不能直接 merge**：39 个文件双方都改过，预演三方合并得到 **26 个冲突（25 个内容冲突 + 1 个 modify/delete）**。
5. **版本号有陷阱**：打了 `10.20.08` 标签的那个提交，其 `csproj` 里 `AppVersion` 仍写着 `10.19.32`；真正自报 10.20.08 的代码在 `future` 分支（比标签多 3 个提交、4 个文件）。

---

## 1. 版本基准确认

### 1.1 上游侧的三个"10.20.08"

| 引用 | 提交 | 时间 | csproj AppVersion | 说明 |
| --- | --- | --- | --- | --- |
| tag `10.20.08` | `41db83f` | 2026-10-24 16:06 | **10.19.32** | 轻量 tag，merge PR #37，实际发布分支 `windows/X86-64` 的 HEAD |
| commit `2573acb` | `2573acb` | 2026-10-24 16:56 | **10.20.08** | 打 tag 50 分钟后才改版本号，并把 TitleBar 的 `-webkit-app-region` 加回来 |
| `future` 分支 tip | `fc49b57` | 2026-10-25 14:14 | **10.20.08** | = tag + 版本号提交 + vitest 依赖 bump（dependabot #38） |

**要点**：如果你拿到的是 tag 对应的构建产物，程序内显示的版本仍是 `10.19.32`。要拿到"自称 10.20.08"的代码，基准应取 `future` tip `fc49b57`。两者差异只有 4 个文件：

```
JiaoLongControl/JiaoLongControl.csproj              (版本号 10.19.32 -> 10.20.08)
JiaoLongControl/Client/src/components/layout/TitleBar.vue  (+2 行 app-region)
JiaoLongControl/Client/package.json                 (@vitest/mocker bump，开发依赖)
JiaoLongControl/Client/package-lock.json
```

### 1.2 本 fork 侧

| 项 | 值 |
| --- | --- |
| 分支 HEAD | `04811f6`（2026-10-13 15:48） |
| AppVersion | **10.19.74**（`JiaoLongControl/JiaoLongControl.csproj`） |
| README 标注 | **10.19.50** —— 已过期，与 csproj 不一致 |
| fork 根提交 | `db4ffe6`「Baseline JiaolongControl windows/X86-64 snapshot」（2026-10-10） |
| fork 根 tree hash | `22e0351…` —— **与上游 `10.19.32` 的 tree hash 完全相同** |
| fork 增量 | 24 个提交、73 个文件、+6572 / −1232 行 |

> fork 根是 10.19.32 的完整快照，因此本报告的全部上游增量 = `git diff 10.19.32 10.20.08`，无需猜测基线。

---

## 2. 上游 6 个提交逐条清点

| 提交 | 日期 | 标题 | 实质内容 | 价值 |
| --- | --- | --- | --- | --- |
| `d6555ae` | 10-09 | 优化界面布局 | 首页三行改 flex 等比分配 + 各卡片 min-height；`MainWindow.xaml` 加 `MinWidth=1300 MinHeight=820` | 中（布局） |
| `524b1b3` | 10-22 | dependabot | vite `8.1.3→8.3.5`、postcss-selector-parser `7.1.7`、source-map-js `1.2.2` | 中（安全） |
| `6354f12` | 10-24 | merge PR #36 | 仅合并上述依赖 | — |
| `cf2c50f` | 10-24 | 添加可编辑值组件 | 新增 `EditableValue.vue` + `utils/number.ts` + 单测；6 个页面接入；新增 `Smu.BootApplyItems` 与 `SelfStart.Smu()` | **高** |
| `f990bae` | 10-24 | "清理无用样式和注释" | **提交信息严重低估**：WMI 电压修复、TDC/EDC 遥测修复、GPU 风扇单位修复、前端枚举错位修复、CPU 预设常量对齐、配置迁移、导航守卫等全在这里，65 个文件 | **最高** |
| `41db83f` | 10-24 | merge PR #37 | 合并 `future` 到发布分支 | — |

> 注意：`f990bae` 标题写"清理无用样式和注释"，实际是本次唯一的高价值修复包。按提交信息筛选会完全错过它。

---

## 3. 新增功能

### 3.1 可点击编辑的数值组件（EditableValue）

- 新文件 `Client/src/components/common/EditableValue.vue`（86 行）+ `Client/src/utils/number.ts`（`applyStep` 吸附/夹取）+ `utils/__tests__/number.spec.ts`（3 条 vitest 用例）。
- 交互：数字默认是文本，单击变成输入框，回车/失焦提交，Esc 取消，提交时按 `step` 吸附并夹在 `[min,max]`。
- 覆盖页面与用法数：`CPU` 6 处、`Osd` 6 处、`GPU` 5 处、`KeyBoard` 5 处、`RyzenSmu` 3 处、`Fan` 2 处。
- 配套：`style.css` 增加 `.arco-modal .arco-input-number` 透明底/主题色描边样式。

### 3.2 SMU 字段级"开机应用"

- 配置新增 `Smu.BootApplyItems: List<string>`。
- `RyzenSmu.vue` 每个数值项旁加"开机"复选框，勾选写入 `BootApplyItems`。
- `SelfStart.Smu()` 按白名单分发：`StapmLimit / StapmTime / FastLimit / SlowLimit / SlowTime / PptLimitRsmu / VrmCurrentMp1 / VrmCurrentRsmu / EdcLimitMp1 / EdcLimitRsmu / TempLimitMp1 / TempLimitRsmu / PboScalar / OcClk / OcVolt`；值为 0 的项跳过并记日志。
- 语义：从"只能整体开机应用 Curve Optimizer"变成"逐项选择开机下发"。

### 3.3 首页布局自适应

- `Home.vue` 由固定高度（`h-[250px]` / `h-[280px]`）改为 `flex flex-col` + `flex-1 min-h-[…]`，三行按窗口高度等比分配。
- `MainWindow.xaml` 增加 `MinWidth="1300" MinHeight="820"`，把"最小窗口"从隐含约定变成硬约束。

### 3.4 测试工程 CLI 重构（仅 `RyzenSmuControllerTest`）

`ArgCursor / CliCommand / CommandRunner / CommandRegistry / HelpText / CpuVidDumper / SensorDumper` 等做参数游标与注册表化，**不影响主程序运行时**。

---

## 4. 修复的问题（按对本机的影响排序）

> 下表"fork 是否中招"为 10-28 分析时的状态；第 1–13 项已在同日回灌修复，详见第 10 节。

| # | 问题 | 位置 | 旧行为 | 新行为 | fork 是否中招 |
| --- | --- | --- | --- | --- | --- |
| 1 | WMI CPU 电压解析错误 | `CpuController` | `(raw & 0xFF)/10`，忽略 bit7 标志位；`0x0189` 会被读成 13.7V | 抽出 `ReadWmiVoltage()`：bit7 置位取高字节，否则取低 7 位 | **是**（两处仍是旧逻辑） |
| 2 | TDC/EDC 遥测取错传感器 | `RyzenSmuController` | 在 `SensorType.Power` 里找名字含 `Core` 的当 TDC；EDC 直接用 `TDC×1.3` 编造 | 改用 `SensorType.Current` 分别匹配 `TDC`/`EDC`；不可用时返回 `null` | **是** |
| 3 | GPU 风扇转速单位错误 | `NvidiaGpuController` | 返回 `CoolerInformation.CurrentLevel`（0–100 的百分比） | 优先取 EC `GPUFanSpeed`（RPM，带 `<10000` 合理性校验），回退按 `MaxFanRpm=6800` 换算 | **是**（页面还标着 "RPM"） |
| 4 | 前端 `SystemPerMode` 下标错位 | `bridge.ts` | `Performance=0, Quiet=1, Balance=2`，与 C# `Balance=0, Performance=1, Quiet=2` 不符 | 对齐 C# | 否（fork 已在 `ce7d7ba` 独立修好，但两侧改法一致） |
| 5 | `AutoFanControl.Stop()` 返回失败 | `AutoFanControl` | 停止成功后返回 `_isRunning`（此时为 false）→ 前端判为失败 | 返回 `true` 并带说明 | 否（fork 已用 `StopLoop()` 重写） |
| 6 | 无 GPU 温度时拿 CPU 温度顶替 | `AutoFanControl` | `rawGpuTemp = 失败 ? smoothedCpuTemp : …`，会按 CPU 曲线驱动 GPU 风扇 | GPU 温度无效时**不写** GPU 风扇，合并曲线只用 CPU | **是**（fork 仍保留回退） |
| 7 | 媒体会话事件解绑错位 | `OsdController` | 订阅 `SessionsChanged += OnMediaSessionsChanged`，退订却用 `-= OnMediaCurrentSessionChanged` | 统一为单一 handler | 否（fork 两个 handler 配对正确） |
| 8 | WebView2 未就绪仍尝试导航 | `MainWindow.xaml.cs` | `if (!envReady) return;` 直接放弃 | 移除该早退，改为 `NavigateWithTimeoutAsync` 内做 generation 守卫 | 部分（fork 有自己的重试链） |
| 9 | 退出清理可能执行两次 | `App.xaml.cs` | 无幂等保护 | `Interlocked.Exchange(ref _cleaned, 1)` | 否（fork 已有 `_cleanupOnce`） |
| 10 | OSD 停止时残留防抖计时器 | `OsdController` | `_pending` 里的 `DispatcherTimer` 未停 | 停止时逐个 `Timer.Stop()` 后清空 | 视 fork 重写而定 |
| 11 | 功耗墙提示值不真实 | `NvidiaGpuController` | 提示里用请求的 `watts` | 用 clamp 后的 `milliwatts/1000` | 是（小问题） |
| 12 | GPU 曲线能力缓存不区分设备 | `NvidiaGpuController` | 单字段 `_curveCapsCache`，多卡串味 | `Dictionary<int, CommandResult>` 按索引缓存，仅成功才缓存 | 是 |
| 13 | 线程亲和性位移溢出 | `RyzenSmuController` | `1L << i` 无上限，>64 逻辑核溢出 | `Math.Min(ProcessorCount, 64)` | 是（7945HX 32 线程不受影响） |
| 14 | 打开链接空串处理 | `SystemInfoController` | `url.Trim() ?? ""`（对 null 无意义） | `IsNullOrWhiteSpace` 直接返回失败 | 是 |
| 15 | 配置反序列化 null 未兜底 | `ConfigSerializer` | 直接返回可能为 null 的对象 | `?? new JiaoLongConfig()` | 是（fork 另有迁移链） |

---

## 5. 变更清单

### 5.1 配置结构（`config.yaml`）

| 变更 | 字段 | 兼容处理 |
| --- | --- | --- |
| 重命名 | `App.BootSetRyzenSumCurveOptimizerAll` → `BootSetRyzenSmuCurveOptimizerAll`（修正 Sum/Smu 拼写） | 反序列化前做字符串替换 `MigrateLegacyKeys()`，旧配置不会丢 |
| 删除 | `Smu.TdcLimitMp1`、`Smu.TdcLimitRsmu` | 本来就没有对应的 SMU 下发方法（10.19.32 里 `SetTdcLimit*` 计数为 0），属死字段 |
| 新增 | `Smu.BootApplyItems: string[]` | 缺省空数组，行为不变 |
| 行为 | `ConfigSerializer.Initialize` 不再"版本号为空就删配置重建" | 改为只走 `Update(existing, version)` |
| 行为 | `Save()` 去掉 `File.Delete(TempPath)` | `File.Move(overwrite:true)` 后临时文件已不存在，属死代码 |

### 5.2 依赖

| 依赖 | 旧 | 新 | 影响 |
| --- | --- | --- | --- |
| vite（dev） | `^8.1.3` | `^8.3.5` | 构建工具，Node 要求不变（`^20.19 \|\| >=22.12`） |
| postcss-selector-parser | 7.1.5 | 7.1.7 | 间接依赖，安全修补 |
| source-map-js | 1.2.1 | 1.2.2 | 间接依赖，安全修补 |
| @vitest/mocker（仅 future 分支） | — | bump | 测试依赖 |
| C# NuGet | — | **零变化** | `csproj` 包引用完全未动 |

### 5.3 文件结构

- 新增 3 个前端文件：`components/common/EditableValue.vue`、`utils/number.ts`、`utils/__tests__/number.spec.ts`。
- **无删除**（服务端、前端均无文件移除）。
- 未新增/修改任何 `Doc/` 文档，上游本次也没更新 README。

### 5.4 语义变更（不看代码不会发现）

| 项 | 旧语义 | 新语义 | 兼容性 |
| --- | --- | --- | --- |
| `NvidiaGpu.GetGpuFanSpeed()` | 百分比 0–100 | RPM | **破坏性**：调用方需按 RPM 解释 |
| `CpuStats.Tdc/Edc` | `double`，取不到给 0 | `double?`，取不到给 `null` | 前端需处理 `null` |
| `GpuStats`（TS 类型） | 含 `GpuTemperature` | 移除该字段 | 与 C# `GpuStatsInfo` 已不一致（后端仍返回） |
| `Keyboard.GetMode/SetMode`、`Power.SetCPUMaxState`、`NvidiaGpu.SetPowerLimit` 等 TS 绑定 | 有声明 | 从 TS 类型映射移除 | C# 方法**仍在**，仅类型层清理 |
| CPU 档位前端默认常量 | `45/60/85/5150` 等 | `Default 45/65/90/4400`、`Performance 65/90/95/4700`、`Saving 30/45/85/3800`、`Custom 45/55/95/4800` | 与 C# 出厂值对齐；**与本 fork 的 7945HX 三档冲突** |
| `config` / `systemInfo` store | 有 `loading` / `error` 状态 | 移除 | 引用了这两个状态的组件会编译失败 |
| 主窗口 | 无最小尺寸 | `MinWidth=1300 MinHeight=820`（DIP） | 与 fork 的物理像素记忆逻辑可能打架 |
| `TitleBar` | 有 `-webkit-app-region` | 标签内删除，50 分钟后在 `future` 又加回 | WebView2 下该属性本就无效，属清理反复 |

---

## 6. 对当前 fork 的影响评估

### 6.1 合并可行性（硬数据）

| 指标 | 数值 |
| --- | --- |
| 上游改动文件 | 73 |
| fork 改动文件 | 73 |
| **双方都改** | **39** |
| **三方合并冲突** | **26**（25 内容冲突 + 1 modify/delete） |
| 可自动合并的重叠文件 | 13 |

冲突文件（26）：

```
Client: RightSide.vue, useFanCurveEditor.ts, constants/index.ts, CPU.vue, Fan.vue,
        General.vue(modify/delete), Home.vue, Home/CoreMonitoring.vue,
        Home/PerformanceMode.vue, Home/WelcomeBanner.vue, Main.vue, Osd.vue,
        Settings.vue, stores/systemInfo.ts, utils/bridge.ts
Server: App.xaml.cs, AutoFanControl.cs, CpuController.cs, FanController.cs,
        NvidiaGpuController.cs, OsdController.cs, RyzenSmuController.cs,
        Native/User32.cs, MethodServices.cs, SelfStart.cs, OsdWindow.xaml.cs
```

> 其中相当一部分冲突是**噪音**：上游把全项目 import 从 `{a, b}` 改成 `{a,b}`、调整空行与注释换行（`RightSide / Main / CpuDie / TitleBar / UiTheme` 等）。真正需要判断的语义冲突约 12 处。

> fork 与上游**没有共同祖先**（fork 根是独立根提交），所以 `git merge upstream/...` 只会把全部文件当成新增冲突。正确做法见第 7 节。

### 6.2 建议吸收（纯收益，风险低）

1. **WMI 电压修复**（`CpuController.ReadWmiVoltage`）——本机 CPU 电压显示会从"偶尔 13.7V"变成正确值或干脆不显示。
2. **TDC/EDC 遥测修复**（`RyzenSmuController`）——SMU 页两个电流表从"编造值"变成真实值；配套把前端 `(telemetry.Tdc ?? 0)` 改成 `N/A`。
3. **GPU 风扇转速改 RPM**——fork 的 GPU 页本来就标着 "RPM"，但拿到的是 0–100，等于显示错误；采纳后自然对齐。
4. **GPU 曲线能力缓存按设备分键**、**功耗墙提示用 clamp 值**、**`GetCoreVoltage` 64 核上限**、**`OpenUrl` 空串校验**——小修小补。
5. **`EditableValue.vue` + `utils/number.ts` + 单测**——纯新增文件，可零冲突引入，再逐页接入。
6. **vite 升级与两条安全补丁**——建议独立提交，便于回滚。

### 6.3 建议拒绝或改写（会伤害 fork 现有语义）

| 上游改动 | 为什么不能照搬 | 建议 |
| --- | --- | --- |
| CPU 档位默认常量（`45/65/90/4400`…） | fork 是 7945HX 专用三档（`45/60/90/4750`、`78/108/95`、`90/115/96`），照搬会让"恢复默认"把机器打回通用值 | 保留 fork 常量，只借鉴其"注释与 C# 对齐"的写法 |
| `AutoFanControl` 去掉 GPU 温度回退 | fork 的 `SharedHeatPipeSyncRatio` 双风扇联动依赖 GPU 温度；无独显温度时上游直接不写 GPU 风扇，会让共享热管场景少一路散热 | 保留回退，但加上上游的"温度合法性校验（0<x<150）"与失败即跳过本轮 |
| `MainWindow` `MinWidth/MinHeight` 硬约束 | fork 有 `WindowPixelWidth/Height` 物理像素记忆，高 DPI 下 1300 DIP 会换算成更大物理像素，与记忆值冲突，可能一开窗就超出屏幕 | 若采纳，改为按 DPI 折算后再夹取，或仅在恢复记忆失败时兜底 |
| `Home.vue` flex 自适应 | fork 首页已重排（场景提示卡、快捷操作、固定高度图表区），直接套用会打架 | 只取"最小高度 + 不裁掉第三行"的思路，手改 fork 版首页 |
| `General.vue` 的改动 | fork 已把"常规"页合并进设置并删除该文件，接受上游等于复活重复入口 | 跳过；其中 `lockSetters` 提升与 `console.warn` 属可选优化，无功能价值 |
| `ConfigSerializer.Initialize` 删除"空版本重建" | fork 的 10.19.34→10.19.72 迁移链依赖 `Initialize → Update`，整段替换会破坏迁移 | 保留 fork 的 `Initialize`，只取 `?? new JiaoLongConfig()` 兜底与 `MigrateLegacyKeys` 思路 |
| `BootSetRyzenSum…` → `…Smu…` 重命名 | fork 有 6 处引用（`JiaoLongConfig.cs`、`SelfStart.cs`、`CpuPowerKeepAliveService.cs`、`Settings.vue` 2 处、`types/config.ts`），只改后端会让设置页开关失效 | 要么全量改名并加迁移，要么整体跳过（旧名不影响运行） |
| `SelfStart.Smu()` 构造期执行 | fork 的 `SelfStart` 有 `EnsureDefaultQuietMode()` + `CpuPowerKeepAlive`，顺序敏感（先对齐 EC Quiet 再绑风扇）；直接插入会打乱开机顺序 | 放到 `EnsureDefaultQuietMode()` **之后**执行，且仅当 `BootApplyItems` 非空 |
| `Smu.TdcLimit*` 字段删除 | fork 的 `types/config.ts` 仍声明这两个字段，后端删了前端会类型不匹配 | 前后端同步删，或都不动 |
| `stores/config.ts`、`systemInfo.ts` 去掉 `loading/error` | fork 的 `App.vue`、`FanCurveEditor.vue` 有 `.loading-state` 样式与状态消费 | 保留 fork 的 store 契约 |

### 6.4 若强行"一律取上游"的具体故障预测

1. 首页"恢复默认/快捷场景"失效或功耗打回 45W——CPU 预设被上游通用值覆盖。
2. 高性能档噪音/温度表现变化——`90/115/96` 被换成 `65/90/95`。
3. 无独显温度时 GPU 风扇不再受控——共享热管联动失效。
4. 设置页"开机应用 Curve Optimizer"开关点了不生效——字段改名后前端仍写旧路径。
5. 启动时静音档对齐被冲掉——`SelfStart` 顺序被上游版本替换。
6. 高 DPI 下窗口开出来超出屏幕或布局裁切——最小尺寸硬约束 + 物理像素记忆冲突。
7. SMU 页 TDC/EDC 显示 `0.0` 而不是 `N/A`——后端返回 `null`、前端仍按 0 处理。
8. 配置迁移断链——`Initialize` 被替换后，老 `config.yaml` 的 OSD/Cpu 迁移不再执行。

---

## 7. 建议的落地顺序

### 批 1：直接回灌的真修复（不动布局、不动预设）

```
Server/Core/Controllers/CpuController.cs       只取 ReadWmiVoltage() 及其两处调用
Server/Core/Controllers/RyzenSmuController.cs  只取 TDC/EDC 传感器分支、MaxAffinityCores、TrySend 名称带 MP1/RSMU
Server/Core/Controllers/NvidiaGpuController.cs 只取 GetGpuFanSpeed、曲线缓存字典、功耗提示、FanSpeed 空值保护
Server/Core/Controllers/SystemInfoController.cs OpenUrl 空串校验
Server/Core/Utils/ConfigSerializer.cs         仅取 ?? new JiaoLongConfig() 兜底
Client/src/pages/RyzenSmu.vue                 Tdc/Edc 的 null -> N/A
```

### 批 2：独立新增，零冲突

```
Client/src/components/common/EditableValue.vue
Client/src/utils/number.ts
Client/src/utils/__tests__/number.spec.ts
Client/styles 里 .arco-input-number 相关片段
```
先落组件与单测，再按 CPU → Osd → GPU → KeyBoard → RyzenSmu → Fan 的顺序逐页替换展示文本。

### 批 3：需要产品决策

- SMU 字段级"开机应用"（`BootApplyItems` + `SelfStart.Smu()`）：功能有价值，但要重排 `SelfStart` 顺序，并决定与 fork 的"静音档对齐/保活"谁先谁后。
- 首页自适应 + 窗口最小尺寸：建议只做"第三行不被裁掉"，不做硬性最小尺寸。
- vite 升级：单独一个 chore 提交。

### 批 4：明确跳过

`General.vue`、CPU 预设常量、`AutoFan` GPU 回退移除、`stores` 状态裁剪、`BootSetRyzenSum` 重命名（除非做全量迁移）。

### 工程做法（因无共同祖先）

不要直接 `git merge upstream/windows/X86-64`。三种可行路径，按推荐度排序：

1. **按提交 cherry-pick**：`git cherry-pick -x d6555ae cf2c50f f990bae`，每个提交单独解冲突，历史清晰、可回滚。注意 `f990bae` 混了 65 个文件的清理，冲突量大。
2. **按文件取片段**：`git diff 10.19.32 10.20.08 -- <file>` 后手工挑选 hunk 应用到 fork（批 1 用这个最省事）。
3. **嫁接历史**：`git replace --graft db4ffe6 10.19.32` 让 fork 根挂到上游 10.19.32 之下，之后就能正常三方合并；缺点是改写了本地历史视图，需要团队约定。

---

## 8. 附带发现（与上游无关，fork 自身问题）

1. **README 版本号过期**：README 三处写 `10.19.50`，`csproj` 实际 `10.19.74`。
2. **GPU 风扇单位错标**：GPU 页写 `RPM`，后端返回的是 0–100，属既有显示缺陷（可由批 1 顺带修好）。
3. **SMU 页 TDC/EDC 无 `null` 处理**：上游返回 `null` 后，fork 当前写法会显示 `0.0`。
4. **`BootSetRyzenSum…` 拼写错误仍在**：fork 保留了上游的 `Sum`（应为 `Smu`），属历史包袱，不影响功能。
5. **fork 的 `AutoFanControl.Stop()` 已自带修复**，但 `RestoreEcAutoFan` 与上游"不关 FAAP"的取舍不同——两条路线需在文档里写清，避免后续误合并。

---

## 9. 证据附录

```bash
# 1) 确认 fork 根就是上游 10.19.32 快照（tree hash 相同）
git rev-parse db4ffe6^{tree}     # 22e035117418ade3b6ee26e7db3d789d02a72f75
git rev-parse 10.19.32^{tree}    # 22e035117418ade3b6ee26e7db3d789d02a72f75

# 2) 上游真实增量
git log --oneline 10.19.32..10.20.08          # 6 个提交
git diff --stat 10.19.32 10.20.08             # 73 files, +1565 -1164

# 3) 标签内版本号与 future 分支对比
git show 10.20.08:JiaoLongControl/JiaoLongControl.csproj | grep AppVersion   # 10.19.32
git show upstream/future:JiaoLongControl/JiaoLongControl.csproj | grep AppVersion  # 10.20.08
git log --oneline 10.20.08..upstream/future   # 3 个提交 / 4 个文件

# 4) 三方合并预演（不触碰工作区）
git merge-tree --write-tree --merge-base=10.19.32 HEAD 10.20.08 | grep '^CONFLICT'

# 5) 双方改动文件交集
comm -12 <(git diff --name-only db4ffe6 HEAD | sort) \
         <(git diff --name-only 10.19.32 10.20.08 | sort)   # 39 个
```

---

## 10. 本次回灌记录（2026-10-28）

> 原则：只回灌**缺陷修复**，不改动本 fork 的任何功能改造（QuietDaily / 7945HX 三档 / SoftSchedule / CpuPowerKeepAlive / OSD / 侧栏 / 设备页等）。
> 验证：`dotnet build -c Release -p:Platform=x64` 0 错误；`npm run type-check` 0 错误；`eslint` 改动文件 0 告警。

### 已回灌（8 处，7 个文件 +121/−47）

| # | 文件 | 内容 |
| --- | --- | --- |
| 1 | `Core/Controllers/CpuController.cs` | 新增 `ReadWmiVoltage()`，替换两处 `(raw & 0xFF)/10`；bit7 置位时取高字节 |
| 2 | `Core/Controllers/RyzenSmuController.cs` | TDC/EDC 改读 `SensorType.Current` 的 `TDC`/`EDC`，取不到给 `null`，删除 `EDC = TDC×1.3`；频率优先 `Core #1`、回退 `Bus Speed`；`GetCoreVoltage` 亲和性上限 64 核 |
| 3 | `Core/Controllers/NvidiaGpuController.cs` | `GetGpuFanSpeed` 改为 EC `GPUFanSpeed`（RPM，`0<x<10000` 校验）优先、NVAPI 档位按 6800 换算回退；`GpuStats.FanSpeed` 空值保护；曲线能力缓存改按 GPU 索引且只缓存成功结果 |
| 4 | `Core/Controllers/AutoFanControl.cs` | CPU 温度获取失败或读数不在 `0–150` 时跳过本轮；GPU 温度同样做合法性校验，**但保留 fork 的 CPU 温度回退**（共享热管同步依赖它） |
| 5 | `Core/Controllers/SystemInfoController.cs` | `OpenUrl` 先 `IsNullOrWhiteSpace` 再 `Trim`，null 参数返回明确错误而非 NRE |
| 6 | `Core/Utils/ConfigSerializer.cs` | `Deserialize` 结果 `?? new JiaoLongConfig()`（含备份文件分支） |
| 7 | `Client/src/pages/RyzenSmu.vue` | TDC/EDC 为 `null` 时显示 `N/A`，不再伪装成 `0.0` |
| 8 | `Client/src/pages/GPU.vue` | 风扇火花线量程 `40 → 58`（满量程 5800 RPM ÷ 100）。这是第 3 项的**配套改动**：单位从百分比变 RPM 后，原量程会让 4000 RPM 以上全部顶格 |

### 未回灌（附理由）

| 上游改动 | 理由 |
| --- | --- |
| `EditableValue.vue` + `number.ts` + 单测 | 新功能（交互改造），不属缺陷修复；需要逐页评估 |
| `Smu.BootApplyItems` + `SelfStart.Smu()` | 新功能；且会打乱 fork `SelfStart` 的"先对齐 Quiet 再绑风扇"顺序 |
| `Smu.TdcLimitMp1/Rsmu` 字段删除 | 纯死字段清理，与 fork 的 `types/config.ts` 需同步，收益为零 |
| `BootSetRyzenSum…` → `…Smu…` 重命名 | fork 有 6 处引用（含设置页配置路径），改名需全量迁移，非缺陷 |
| CPU 预设常量（45/65/90/4400…） | 会覆盖 7945HX 三档（78/108/95 等），属功能回退 |
| 首页 flex 自适应 + 窗口最小尺寸 | 与 fork 已重排的首页、DPI 物理像素记忆冲突，属设计取舍 |
| `General.vue` 改动 | fork 已把该页并入设置并删除文件，接受等于复活重复入口 |
| `ConfigSerializer.Initialize` 重写 | 会切断 fork 10.19.34→10.19.72 的迁移链 |
| `stores/config.ts`、`systemInfo.ts` 去掉 `loading/error` | fork 有消费方，删了会编译失败 |
| `TrySend` 名称加 `[MP1 0x..]` 后缀 | 改变用户可见的失败提示文案，属诊断信息而非修复 |
| `OsdController` 媒体事件合并 | fork 的订阅/退订本就配对正确，无双绑泄漏 |
| `App.xaml.cs` 清理幂等、`AutoFan.Stop()` 返回值 | fork 已各自实现等价修复 |
| `ReleaseNotesMarkdown` / `MethodServices` / `FanController` / `User32` / `CpuDie` / `TitleBar` / `RightSide` / 测试工程 CLI | 纯清理与格式化，无行为价值 |
| `MainWindow.xaml.cs` 导航守卫 | 与 fork 自己的重试链耦合，改动风险大于收益 |
| vite `8.1.3 → 8.3.5` 与两条安全补丁 | 构建期依赖，不影响发布产物；建议单独一个 chore 提交处理 |

---

*第 1–9 节为 2026-10-28 的比对快照；第 10 节记录当日实际回灌结果，两处结论冲突时以第 10 节为准。*
