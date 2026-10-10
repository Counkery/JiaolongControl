<h1 align="center">JiaoLongControl</h1>

<p align="center">
  <strong>蛟龙 16 Pro 2023 笔记本控制中心（改造 fork）</strong><br>
  <em>针对 7945HX + RTX 4060 开发；其它 16 Pro 2023 机型理论可试，风险自负</em>
</p>

<p align="center">
  <a href="https://github.com/Counkery/JiaolongControl">
    <img src="https://img.shields.io/badge/fork-Counkery-181717?logo=github" alt="Fork">
  </a>
  <a href="https://github.com/GaoXanSheng/JiaolongControl">
    <img src="https://img.shields.io/badge/upstream-GaoXanSheng-555?logo=github" alt="Upstream">
  </a>
  <img src="https://img.shields.io/badge/version-10.19.47-blue" alt="Version">
  <img src="https://img.shields.io/badge/.NET-8.0_WPF-512BD4?logo=dotnet" alt=".NET">
  <img src="https://img.shields.io/badge/Vue-3.5-4FC08D?logo=vuedotjs" alt="Vue">
  <img src="https://img.shields.io/badge/WebView2-前端宿主-0078D7?logo=microsoftedge&logoColor=white" alt="WebView2">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
  <a href="https://qm.qq.com/q/4ase4LoAJi">
    <img src="https://img.shields.io/badge/QQ%20群-蛟龙工具箱问题反馈-EB1923?logo=tencentqq&logoColor=white" alt="QQ Group">
  </a>
</p>

本仓库是 [GaoXanSheng/JiaolongControl](https://github.com/GaoXanSheng/JiaolongControl) 的 fork。日常开发在 **`remake/jiaolong-control`**。当前版本 **10.19.47**（见 `JiaoLongControl/JiaoLongControl.csproj` 的 `AppVersion`）。

架构：.NET 8 WPF 宿主 + WebView2 + Vue 3。硬件走 **PawnIO**（Ryzen SMU）和随包 **Blding / `JiaoLongDriver64`**（EC / 风扇），不用 WinRing0。

请勿与闭源「第三方蛟龙游戏控制中心」、JL16ProAiFan、nbfc 等同类温控同时运行。

---

## 能做什么

| 页 | 能力 |
| --- | --- |
| 主页 | EC 三档（办公 Quiet / 均衡 Balance / 狂暴 Performance）、「一键安静日常」、模式绑定 CPU 档与风扇上限 |
| 中央处理器 | CustomMode 功耗（SPL/SPPT）、温度墙、频率上限、睿频；A/B/C 预设与软调度 SoftSchedule |
| 风扇 / 温度曲线 | WMI 转速上限（官方路径）；软件曲线 AutoFan（进阶）；升/降速斜率；满速风扇开关 |
| 图形处理器 | 独显直连 / 混合、锁频、偏移、功耗墙（有安全范围） |
| Ryzen SMU | Curve Optimizer 等；与 EC 功耗墙不要同时猛调 |
| 键盘 / OSD / 常规 | RGB 与 Logo 灯、FN/触控板锁、OSD、各类锁定开关 |
| 设备信息 | CPU / GPU / 内存 / 磁盘遥测 |
| 设置 | 开机自启、启动最小化、驱动模式（优先 PawnIO）、导入旧版 `AppSettings.json` |

配置文件：程序目录 `config/config.yaml`。

### 7945HX 三档预设（EC）

切性能模式且 `App.ApplyCpuProfileOnModeChange=true` 时同步对应 CPU 档。细节见 [Doc/CPU_PRESETS_7945HX.md](Doc/CPU_PRESETS_7945HX.md)。

| 档 | EC 模式 | 长期 / 短期 | 温度墙 | 风扇上限 |
| --- | --- | --- | --- | --- |
| A 办公静音 | Quiet | 45 / 60 W | 90 ℃ | 2700 RPM |
| B 游戏均衡 | Balance | 78 / 108 W | 95 ℃ | 3500 RPM |
| C 生产力 | Performance | 90 / 115 W | 96 ℃ | 5800 RPM（WMI 上限区间 5000–5800） |

软调度（powercfg：EPP / 最少核心 / 睿频）随切模式、CPU「应用」、开机写入**当前**电源方案，不整包导入 `.pow`。

### 一键安静日常

瞬时组合：均衡 + 风扇上限 3500 RPM + 78/108 W + 93 ℃ + QuietDaily 软调度。不覆盖用户的 `Cpu.Default`。同模式再点不会退出。退出方式：切到其它性能模式，或 CPU 页「应用」/重置，或其它 **CPU 相关** 配置下发。改主题、OSD 等不会退出。

### 软件风扇曲线（进阶）

默认仍用官方 WMI **上限**。软件曲线按配置限速（默认升 600 RPM/s、降 250 RPM/s，范围 50–2000）。停止曲线、退出程序或未处理异常时走 `RestoreEcAutoFan`：目标转速写 0 并关掉 FAAP，交回 EC 自动。

---

## 使用

1. 管理员权限运行（清单要求，否则写不了 EC / SMU）。
2. 需要 SMU 时安装 [PawnIO](https://pawnio.eu/) 运行时。
3. 启动后托盘有图标；设置页可开开机自启和启动最小化。
4. 改硬件参数前先理解含义；建议备份 `config.yaml`。Curve Optimizer 从很小的负值开始逐核试，过度会蓝屏或死机。

本 fork 的便携包见 [Releases](https://github.com/Counkery/JiaolongControl/releases)（`JiaoLongControl-*-win-x64.zip`）。解压后**管理员**运行 `JiaoLongControl.exe`；SMU 需先装 [PawnIO](https://pawnio.eu/)。请先退出闭源第三方控制中心再启动。上游安装包见 [GaoXanSheng Releases](https://github.com/GaoXanSheng/JiaolongControl/releases)，功能与本分支不一致。

---

## 开发

需要：.NET 8 SDK、Node.js 20.19+ 或 22.12+、x64 Windows。没有解决方案 `.sln`，直接编工程文件。

```bash
# 前端（生产资源进 WebRoot）
cd JiaoLongControl/Client
npm install
npm run build

# 宿主
dotnet build JiaoLongControl/JiaoLongControl.csproj -c Release -p:Platform=x64

# 发布
dotnet publish JiaoLongControl/JiaoLongControl.csproj -c Release
```

前端热更新：`npm run dev`（Vite `localhost:5173`），调试模式下 WebView2 指向该地址。

### 版本号

唯一来源是 `JiaoLongControl/JiaoLongControl.csproj` 的 `AppVersion`（当前 `10.19.47`）。

| 段 | 含义 |
| --- | --- |
| 第一位 | 破坏性更新 |
| 第二位 | 功能 |
| 第三位 | 修复 |

按改动类型加对应段，后面的段归零。

---

## 与上游原项目的差异

上游：[GaoXanSheng/JiaolongControl](https://github.com/GaoXanSheng/JiaolongControl)（分支 `windows/X86-64`）。本 fork 在其架构上针对 7945HX + 4060 做稳定性与日常可用性改造，**未改协议栈选型**（仍是 PawnIO + Blding + WMI）。

| 维度 | 上游 | 本 fork（10.19.47） |
| --- | --- | --- |
| 性能模式 | Quiet / Balance / Performance | 同左 + **一键安静日常**（瞬时功耗/风扇/软调度，不写坏 Default） |
| CPU 预设 | 通用配置字段 | **7945HX A/B/C**（45/78/90W 档）+ 切模式联动 |
| 软调度 | 偏裸 `EnableTurbo` | **SoftSchedule**（EPP / 最少核心 / Boost AC·DC） |
| 功耗保活 | 有基础能力 | 与监控轮询分离；**世代 / Suppress / Detect 竞态**加固 |
| 风扇语义 | 上限与设速易混 | **WMI 上限优先**；软件曲线标为进阶 |
| AutoFan | 有曲线 | **可配置升/降速斜率**（默认 600/250 RPM/s） |
| 退出行为 | 一般清理 | **`RestoreEcAutoFan`**（写 0 + 关 FAAP），停曲线/退出/崩溃都交回 EC |
| WMI 热键 | 可能重复 OSD | **去重**，切模式与 Detect 互不冲掉 QuietDaily |
| RGB | 有键盘灯 | **即时关灯**，渐变不回写旧色 |
| 配置迁移 | YAML | 可 **导入** 闭源控制中心 `AppSettings.json` |
| 电池阈值 | 无 | 设置页 **外挂** [Battery-threshold](https://github.com/Zcodeoooo/JiaoLong16Pro-Battery-threshold)（不内嵌 WinRing0） |
| 设备信息 | 主页部分遥测 | 独立 **设备信息**页（CPU/GPU/内存/磁盘） |
| 前端打包 | 常规 | **按路由懒加载**拆包 |
| 日志 | 默认偏多 | Info + 轮转 |
| GPU 偏移 | 有 | **安全范围护栏** |

明确不做、也不打算并进控制台的：刷写 EC 风扇表固件、WinRing0、UXTU/ryzenAdj 整栈、与 JL16ProAiFan 双开。

通用修复可向上游开 PR；见 [Doc/CONTRIBUTING_UPSTREAM.md](Doc/CONTRIBUTING_UPSTREAM.md)。不要把本机路径写进上游补丁。

---

## 参考项目

同机型（MRID6 / 蛟龙 16 Pro 2023）生态，改造时对照过这些仓库。完整矩阵见 [Doc/ECOSYSTEM_COMPARE.md](Doc/ECOSYSTEM_COMPARE.md)。

| 项目 | 用途 | 与本项目关系 |
| --- | --- | --- |
| [GaoXanSheng/JiaolongControl](https://github.com/GaoXanSheng/JiaolongControl) | 上游开源控制台 | **底座**；本仓是其 fork |
| [Zcodeoooo/JiaoLong16Pro-Battery-threshold](https://github.com/Zcodeoooo/JiaoLong16Pro-Battery-threshold) | 电池充放电阈值（EC 邮箱） | 设置页外挂调用；专补电池，不替代控制台 |
| [zhaob1n/mechrevo-jiaolong16pro-fan](https://github.com/zhaob1n/mechrevo-jiaolong16pro-fan) | 风扇/性能协议调研 + Linux WMI | 纠正「官方无曲线、WMI 只设上限」；安静日常（均衡+3500）依据其实测 |
| [GaoXanSheng/JiaoLong16Pro-EC_Modifier](https://github.com/GaoXanSheng/JiaoLong16Pro-EC_Modifier) | 离线刷改 EC 风扇表 | **高风险**；控制台内不集成，仅文档链接 |
| [sakura-zsh/mrid6-fans-controler](https://github.com/sakura-zsh/mrid6-fans-controler) | Linux EC 目标转速 + 斜率 + 退出恢复 | AutoFan **斜率**与退出交回 EC 的设计参考 |
| [dfwsbylwy/JL16ProAiFan_MRID6-23](https://github.com/dfwsbylwy/JL16ProAiFan_MRID6-23) | Windows 软件温控轮询改上限 | 与 AutoFan/上限同类；**勿与本程序双开** |
| [kzyha/jiaolong16-linux](https://github.com/kzyha/jiaolong16-linux) | 2022 模具 Linux 控制 | 灯/锁/MUX 可参考；风扇结论勿照搬到 2023 |
| 闭源「第三方蛟龙游戏控制中心」 | 同族 WMI 成品控制台 | 协议/字段对照样本；勿双开；见 [Doc/FEATURE_GAP_THIRDPARTY.md](Doc/FEATURE_GAP_THIRDPARTY.md) |

协议要点（来自调研仓）：官方路径是 **转速上限**（FAAP/FASP），真实温控曲线在 EC 固件里；软件曲线是进阶覆盖，不是默认。

其它文档：

- [Doc/CPU_PRESETS_7945HX.md](Doc/CPU_PRESETS_7945HX.md) — 预设与安静日常
- [Doc/PROTOCOL_MAP.md](Doc/PROTOCOL_MAP.md) — WMI / EC 对照
- [Doc/BASELINE_CHECKLIST.md](Doc/BASELINE_CHECKLIST.md) — 真机验收
- [Doc/PHASE3_SMU_GPU.md](Doc/PHASE3_SMU_GPU.md) — SMU / GPU 阶段说明

---

## 许可证

[MIT](LICENSE.md) © 2025 GaoXanSheng

本 fork 的改造同样按 MIT 分发。使用风险自负。非官方工具，与机械革命 / 清华同方无关。
