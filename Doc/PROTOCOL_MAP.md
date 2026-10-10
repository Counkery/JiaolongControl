# 第三方控制中心 ↔ JiaolongControl 协议对照

对照样本：本地闭源 `D:\Application\JiaoLong7.3`（第三方蛟龙游戏控制中心 0.3.10）。  
实现落点：开源 `JiaoLongControl/Server`。

## WMI / EC 热键事件 (`HID_EVENT20`)

| 第三方日志 `wMIEventName` | JiaolongControl `EventName` | 值含义 | 处理 |
|---|---|---|---|
| SystemPerMode | `SystemPerMode` (15) | 0=均衡, 1=性能, 2=安静 | OSD + 可选风扇绑定 |
| TouchPadState | `TouchPadState` (6) | 0/1 | OSD / `SetTouchpadLock` |
| FnState | `FnState` (7) | 0/1 | OSD / `SetFnLock` |
| RGB 亮度相关 | `RGBKeyboardBrightness` (5) | 0–3 | OSD |
| （风扇 RPM 事件） | `CPUFanSpeed` / `GPUFanSpeed` | 双字节 RPM | 监控用，**不去重** |

第三方常见双发：同一热键连续两条相同事件。已在 `WMIEventService` 对非风扇事件做 250ms 去重。

## MethodName（EC 方法）

| 能力 | `MethodName` | 第三方符号痕迹 |
|---|---|---|
| 性能模式 | `SystemPerMode` | `SetSP_PerformaceMode` / WMISystemPerMode |
| 独显直连 | `GpuMode` | `SetSP_GraphicsDirect` |
| RGB 模式/颜色/亮度 | `RGBKeyboardMode/Color/Brightness` | `SetSP_Keyboard*` |
| FN / 触控板 | `FnLock` / `TPLock` | `SetSP_FNLock` / `SetSP_TPMode` |
| 风扇读写 | `CPUGPUFanSpeed` + Blding EC | `Set_FanSpeed` / `_CPUGPUFanSpeed` |
| CPU 功耗/温度墙 | `CPUPower` + `CPUPower.*` | `Set_SPLSlider` / `Set_SPPTSlider` / `Set_CPUTempWall` |

## 性能模式枚举

| 第三方 | JiaolongControl `SystemPerMode` |
|---|---|
| WorkMode / 办公安静 | `QuietMode` |
| PlayingMode / Balance | `BalanceMode` |
| FastestMode / 狂暴 | `PerformanceMode` |
| CustomMode | 软件侧自定义功耗档 `Cpu.CpuProfile=custom`（非 EC 模式枚举） |

## 配置字段映射（AppSettings.json → JiaoLongConfig）

| 第三方 | JiaolongControl | 说明 |
|---|---|---|
| WorkModeFanSpeedSaveValue (%) | `Fan.QuietModeFanRpm` | % → RPM≈%×58 |
| PlayingModeFanSpeedSaveValue | `Fan.BalanceModeFanRpm` | 同上 |
| FastestModeFanSpeedSaveValue | `Fan.PerformanceModeFanRpm` | 同上 |
| SplData | `Cpu.*.CpuLongPower` | 长期功耗 W |
| SpptData | `Cpu.*.CpuShortPower` | 短期功耗 W |
| CpuTempWallData / CpuTemperatureWall | `Cpu.*.CpuTempWall` | ℃ |
| ExeStart | 开机自启（任务计划） | `AutoStartController` |

导入 API：`ConfigCtrl.ImportLegacyAppSettings(path)`（空路径尝试 `D:\Application\JiaoLong7.3\AppSettings.json`）。

## 驱动

| 路径 | 说明 |
|---|---|
| 第三方 WinRing0 | **避免**；Defender/CVE 风险 |
| JiaolongControl `Drivers/PawnIO` | 默认优先（`App.PreferPawnIO`） |
| JiaolongControl `Drivers/Blding` | 本机 EC/风扇 |

## CPU 三档预设 (7945HX / 10.19.34+)

详见 [CPU_PRESETS_7945HX.md](./CPU_PRESETS_7945HX.md)。

| EC `SystemPerMode` | CpuProfile | 含义 |
|---|---|---|
| QuietMode | `saving` | 办公静音 45/60W |
| BalanceMode | `default` | 游戏均衡 78/108W |
| PerformanceMode | `performance` | 生产力 90/115W |

`App.ApplyCpuProfileOnModeChange`：UI 与 EC 热键切模式时同步下发功耗/温度墙（`PerformanceModeBindings`）。

## 使用约定

- 不要与闭源控制中心同时控制同一硬件。
- 监控/OSD 轮询只读；功耗重应用走 `CpuPowerKeepAliveService`。
