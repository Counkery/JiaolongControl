# Phase 3 — SMU / GPU / 独显 / 工程化

本 fork 已落地安全护栏与文档；深度调校以「真机小步 + 可回滚」为准。

## Ryzen SMU

- UI：`Client/src/pages/RyzenSmu.vue`
- 后端：`RyzenSmuController` + `SmuFamilyResolver`（Dragon Range 等）
- 建议预设（写入 `Smu` 段前先备份 YAML）：
  - **日常**：STAPM/Fast/Slow 贴近 CPU 页 Long/Short
  - **游戏**：略抬 Fast/Slow，温度墙 ≤ 95℃
  - **CO**：全核从 -5 起测，分核谨慎
- 自测：`RyzenSmuControllerTest` CLI

## GPU

- `NvidiaGpuController.SetGpuOffsets`：拒绝 |core|>250 或 |mem|>1000 的偏移（安全提示范围）
- 开机高级偏移默认关（`BootGpuUseAdvancedOffsets=false`）
- NVPCF/解锁 DB：仅在充分了解风险后使用；提供清零回滚

## 独显直连

- UI：`Settings/components/GPUDirectConnection.vue`
- EC：`MethodName.GpuMode`（Hybrid / Discrete）
- 切换前应提示：可能黑屏/需注销；勿在游戏全屏中切换

## 发布精简

- 保持 framework-dependent 或单文件按需，**不要**塞入完整 PowerShell 运行时
- 驱动仅保留 `Drivers/PawnIO` + `Drivers/Blding`
- 日志默认 Info + 14 天/5MB 轮转（见 `App.config`）

## 可向上游贡献的修复

建议拆成独立小 PR：

1. `WMIEventService` 热键去重  
2. `CpuPowerKeepAliveService` 功耗保活（与轮询分离）  
3. 键盘关灯 `StopImmediate`  
4. log4net 轮转与默认 Info  
5. 模式-风扇绑定与 Legacy AppSettings 导入  

保留 MIT 版权声明与上游作者信息。
