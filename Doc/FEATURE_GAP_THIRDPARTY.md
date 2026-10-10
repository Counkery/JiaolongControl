# 第三方控制中心 ↔ JiaolongControl 功能缺口

对照样本：`D:\Application\JiaoLong7.3`（第三方蛟龙游戏控制中心 **0.3.10**，主 DLL / `AppSettings.json` / 符号 / ErrorLog）。  
本项目：`D:\Application\JiaolongControl`（改造版 10.19.35+）。  
二次核验：[第三方功能对照梳理](42e307d8-b751-4e3b-ad0c-f3bf943c4798)。

## 第三方有、本项目没有或偏弱

| 功能 | 第三方证据 | 本项目现状 | 建议优先级 |
|---|---|---|---|
| **EC 自定义功耗模式** | `SetSP_CustomMode` / `IsInCustomMode`；日志 `CustomMode:CloseState` | 后端有 `SetCustomMode`（写 `CPUPower.Open/Close`），bridge 已暴露；**前端无入口**；下发 Long/Short 时也**未先 Open CustomMode** | **高** |
| **满速风扇开关（MaxFan）** | `SetSP_MaxFanSpeed` / `MaxFanSpeedButton`；日志 `Set_MaxFanSpeedSlider` | `SetMaxFanSpeedSwitch` 标 Obsolete，bridge 有 API；**Fan.vue 未暴露** | 中 |
| **温度曲线模式页 + 开机自启 UX** | `TemperatureCurvePage` / `TemperatureCurveAutoStart` / `TcListLocal` / `AutoModeStatus` | 有 FanCurveEditor + AutoFan + `BootAdvancedFanControlSystem`；**无同名模式页与 TcList 点阵 UX** | 中 |
| **设备信息专页** | `DeviceInfoPage`、`SetAP_CPUInfos` / `DISKInfos` / `MEMInfos` | 主页有部分遥测；**无磁盘/内存专页与多图表 Info** | 中 |
| **磁盘/内存监控曲线** | 同上 + `DISKPoly` | LHM 有读取路径；**无同等曲线 UI** | 低 |
| **狂暴/高功耗风险提示** | `FastestTipsDialog` / `PowerTips` | 切 Performance 无对等提示 | 低 |
| **CLI 外部设置入口** | `CLISetGpuMode` / `CLISetPerformaceMode` / `CLISetLogoLight` | 无命令行/IPC 对外设模式 | 低 |
| **飞行模式状态** | `AirPlaneMode`；本仓 `EventName.AirPlaneMode=4` | **仅枚举，无业务处理** | 低 |
| **KaronOC GPU OC 栈** | `KaronOC.dll` / `GamingControlCenter.KaronOC` | NvAPI 锁频/偏移/功耗；**非 Karon 曲线栈** | 视稳定性 |
| **UXTU / ryzenAdj 桥** | `RyzenAdj_To_UXTU` / `ryzenAdjCommand` | 自有 RyzenSmu + PawnIO；**有意不跟** | 低 |
| **WinRing0 MSR/IO** | `WinRing0x64.sys` | **刻意不用**（PawnIO/Blding） | — |
| **CPU 电压独立 Toggle** | `CPUVoltage_ToggleButton` / `VoltageStatus` | 以 Curve Optimizer 为主 | 低 |
| **PLL / SLL / PLNum** | `AppSettings.PLLStatus` / `SLLStatus` / `PLNumValue` | **无对等控件**；语义待核实 | 待核实 |
| **GPU 锁频独立开关 UX** | `GPUFreqLock` / `GPUFrequencyRegulation` | 有 Lock/Reset；双开关 UX 不完全一致 | 低 |
| **OSD 图标状态写入 EC** | `Set_OSDIconState` | 自研 OSD；无该 EC 图标位 | 低 |
| **图片预览/打赏窗** | `ImageViewWindow` | 无（非控制功能） | 无 |
| **多语言资源包** | `zh-CN`/`en`/… | 基本中文单语 | 低 |

### ErrorLog 实际出现过的调用（全量扫描约 11 类）

`Init_SettingsAsync` / `Init_SettingsAsync2` / `Init_Keyboard_State` / `SaveAppSettings` / `Set_MaxFanSpeedSlider` / `SetSP_FNLock` / `SetSP_TPMode` / `wMIEventArrived` / Caps on·off；事件：`SystemPerMode` / `FnState` / `TouchPadState`；状态串：`GPUMode:DiscreteMode`、`SystemPerMode:*`、`CustomMode:CloseState`。  
其余大量 `SetSP_*`/`Set_*` 以 **DLL 符号**为准，近期日志未全覆盖。

## 本项目已有对等能力

- 性能模式 Quiet/Balance/Performance（≈ Work/Playing/Fastest）+ 模式绑风扇/CPU 档  
- CPU 长期/短期功耗、温度墙、睿频、频率上限  
- 风扇手动转速、自动曲线、开机高级风扇  
- 键盘 RGB / 渐变、Logo 灯（Ambientlight）  
- 独显直连 / 混合、GPU 锁频/偏移/功耗墙  
- Ryzen SMU（深于第三方表面档）  
- FN / 触控板锁、Caps/Num OSD  
- 开机自启、托盘、主题、YAML、`ImportLegacyAppSettings`

## 本项目独有

- Vue/WebView2 + **按路由懒加载拆包**  
- **CPU 功耗保活**、**WMI 热键去重**  
- **7945HX A/B/C 预设**与模式联动  
- 优先 **PawnIO**、结构化配置、日志轮转  

## 已落地（10.19.36）

1. **高**：`EnsureCustomModeOpen` + CPU 页 CustomMode 开关；应用功耗时先开门  
2. **中**：Fan 页 MaxFan 开关；温度曲线页文案/开机自启；设备信息页（CPU/GPU/内存/磁盘/卷）  

## 仍可不做

- WinRing0、UXTU、Karon 整栈复刻（除非 NvAPI 路径实测不够）
