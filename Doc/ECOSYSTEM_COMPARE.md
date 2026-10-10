# 同机型生态对照：三开源 + 本 Fork + 第三方控制中心

机型锚点：机械革命 **蛟龙 16 Pro 2023**（MRID6 / BM5235，IT5570 EC，7945HX + 4060）。  
对照日期：2026-10-10。

| 代号 | 项目 | 定位 |
|---|---|---|
| A | [Zcodeoooo/JiaoLong16Pro-Battery-threshold](https://github.com/Zcodeoooo/JiaoLong16Pro-Battery-threshold) | 第三方控制台作者开源的 **电池充电阈值**（原生 EC 邮箱） |
| B | [zhaob1n/mechrevo-jiaolong16pro-fan](https://github.com/zhaob1n/mechrevo-jiaolong16pro-fan) | **风扇/性能协议调研** + Linux `bitland-mifs-wmi` 补丁（反编译官方 0.3.15） |
| C | [GaoXanSheng/JiaoLong16Pro-EC_Modifier](https://github.com/GaoXanSheng/JiaoLong16Pro-EC_Modifier) | **离线刷改 EC 风扇表**（永久改固件曲线） |
| D | 本仓库 JiaolongControl Fork（10.19.36） | Windows 全功能控制台（WMI + Blding + SMU + Vue） |
| E | 本地闭源「第三方蛟龙游戏控制中心」0.3.10 | 用户态日常控制台（与官方 0.3.15 同族协议） |

---

## 1. 一句话区别

| 项目 | 改什么 | 是否常驻 | 风险 | 能否替代控制台 |
|---|---|---|---|---|
| A 充电阈值 | EC 电池策略邮箱（`0x10/0x0D`） | 否（提交后由 EC 自管） | 中（WinRing0/内核隔离） | 否，专补电池 |
| B 风扇调研 | 文档 + Linux 驱动补丁；弄清 **WMI 只设转速上限** | Linux 模块常驻 | 低～中（内核模块） | Linux 侧部分替代 |
| C EC 修改器 | **刷写 EC 固件内风扇曲线表** | 否（离线工具） | **高**（砖 EC 风险） | 否，永久改硬件行为 |
| D 本项目 | Windows 用户态全栈（模式/功耗/风扇/GPU/SMU/OSD） | 是 | 中（驱动+误设） | 是，目标形态 |
| E 第三方控制中心 | 同族 WMI + KaronOC +（本机包含 WinRing0 痕迹） | 是 | 中～高（WinRing0/刷屏 Init） | 是，闭源成品 |

---

## 2. 协议真相（以 B 的反编译 + ACPI 为准）

这对 D/E 的「风扇曲线」叙事非常关键：

1. **官方/第三方控制中心没有「上传温度-转速曲线表」到 EC。**  
   风扇只做两件事：`MaxFanSpeedSwitch`（0x14 / FAAP）+ `MaxFanSpeed`（0x15 / FASP，单位 **100 rpm**）= **当前模式下的转速上限**。
2. **真实温控曲线在 EC 固件里**；主机改不了起转延迟、升降斜率。
3. **模式绑定的允许上限范围**（官方常量，与实测一致）：

| 模式 | ITSM | FASP 范围 | 典型默认 |
|---|---|---|---|
| 安静 | 2 | 22–35（2200–3500） | ~27 |
| 均衡 | 0 | 35–50（3500–5000） | ~43 |
| 狂暴 | 1 | 50–58（5000–5800） | ~53 |

4. **OEM THMP 功耗表（R9 / DC）**：安静 45/45/55、均衡 85/90/120、狂暴 101/105/120，温度墙均为 **99℃**。  
   满载常被温度墙卡在 ~75–80W，故狂暴相对均衡性能增益极小、风扇却明显更吵（B 测：均衡+3500rpm 全核约 −2%）。
5. **CustomMode（0x17）**：需圆口 DC 且适配器 **>200W**；再写 SPL / fPPT / 温度墙。退出可恢复进入前模式。
6. **KaronOC** 只做 NVAPI 时钟偏移，与 EC/风扇无关。

C 项目则直接改 EC bin 里 `0x7144~` 风扇表（刷入后全局限速如 2700），属于另一层「改固件」路径。

A 项目走电池命令邮箱（如 `8150=0x10`、`8151=0x0D`，高低阈值写 `8153/8154`），与风扇/功耗 WMI **正交**。

---

## 3. 与本项目（D）对照：能力矩阵

| 能力 | A | B | C | D 本项目 | E 第三方 |
|---|---|---|---|---|---|
| 性能三档 SystemPerMode | — | Linux sysfs | — | ✅ | ✅ |
| CustomMode + SPL/SPPT/墙 | — | Linux sysfs | — | ✅（已 Ensure 开门） | ✅ |
| WMI 转速**上限** 0x15 | — | ✅ 一等公民 | — | ⚠ 有枚举/开关，**UI 语义仍偏「设转速」** | ✅（各模式一滑块） |
| 主机侧软件风扇曲线（轮询） | — | 认为收益有限 | — | ✅ AutoFan + 曲线编辑（走 **Blding**） | ❌（官方无曲线） |
| 刷 EC 风扇表 | — | — | ✅ | ❌（有意不做） | ❌ |
| 电池充放电阈值 | ✅ 原生 EC | — | — | ❌ | ❌ |
| Ryzen SMU / CO 深度档 | — | — | — | ✅ | 弱 / UXTU 痕迹 |
| GPU 偏移 / 锁频 | — | — | — | ✅ NvAPI | ✅ KaronOC |
| 独显直连 | — | Linux gpu_mode | — | ✅ | ✅ |
| 功耗保活 / 热键去重 | — | 休眠恢复上限 | — | ✅ | ❌（Init 刷屏） |
| 驱动栈 | WinRing0（原生工具） | Linux WMI | 刷写工具 | **PawnIO + Blding** | WinRing0 + WMI |

---

## 4. 对本项目最有价值的纠偏（优化方向）

### P0–P2 落地状态（10.19.37）

1. ✅ 风扇页改为 WMI **转速上限**；`SetFanCeilingRpm` + 模式范围钳制；切模式重下发。  
2. ✅ Blding/温度曲线标为**进阶**；与上限路径分离提示。  
3. ✅ CustomMode：`GetAcPowerState` + 非 AC 拒绝开门。  
4. ✅ 主页一键 **安静日常**（均衡 + 3500 + 墙 93）。  
5. ✅ 设置页电池阈值外挂 NativeEnable/Disable（不内嵌 WinRing0）。

### P3 — 明确不做（来自 C）

7. **EC 风扇表刷写**  
   - 保留为高级离线工具链文档链接即可；控制台内集成刷固件风险不可接受。  
   - 若用户已刷 V55 类「全局 2700」EC，本程序的上限/曲线行为会整体偏移，需在 FAQ 说明。

### P4 — 工程卫生（相对 E）

8. 继续保持：监控与下发分离、WMI 去重、功耗保活、PawnIO 优先、勿双开 E。  
9. 可吸收 B 的「按模式记忆风扇上限」到 `Fan.Quiet/Balance/PerformanceModeFanRpm`（已有字段）——确保走的是 **0x15 上限** 而不仅是 Blding 设速。

### 2026-10 补充对照（五仓）

| 仓 | 要点 | 对本项目 |
|---|---|---|
| [zhaob1n/mechrevo-jiaolong16pro-fan](https://github.com/zhaob1n/mechrevo-jiaolong16pro-fan) | 官方无曲线；WMI 只设上限；**均衡+3500≈狂暴−2%**；THMP SPL 狂暴 101/均衡 85、墙 99 | 已落地安静日常；墙 93 仍优于 OEM 99 |
| [sakura-zsh/mrid6-fans-controler](https://github.com/sakura-zsh/mrid6-fans-controler) | Linux EC 直写目标转速 + **升/降速斜率** + 退出恢复自动；部分 BIOS 上 WMI 上限无效 | Win 侧继续 WMI 优先；AutoFan 可借鉴 ramp；退出路径写 0 恢复 |
| [dfwsbylwy/JL16ProAiFan_MRID6-23](https://github.com/dfwsbylwy/JL16ProAiFan_MRID6-23) | 温控轮询改「最大风扇转速」间接曲线；需 V33/V35 类 BIOS | 与 AutoFan/上限双轨同类；勿与本程序双开 |
| [kzyha/jiaolong16-linux](https://github.com/kzyha/jiaolong16-linux) | 2022 模具：风扇无写入；EC 直写灯/锁/MUX；独显按需屏蔽 | 2023 有风扇通道，勿照搬「风扇不可控」结论；灯/锁已有 |
| washyda/mrid6-control-center | Rust 控制中心骨架 | 可参考工程结构，非协议权威 |

**更优思路**

1. ✅ **风扇斜率限速（ramp）**：10.19.47 AutoFan 按配置 `RampUp/DownRpmPerSec`（默认 600/250）限速，按实际 Δt 计算。  
2. ✅ **进程退出恢复 EC 自动**：停止曲线 / 退出 / 崩溃走 `RestoreEcAutoFan`（Blding 写 0 + 关 FAAP）。  
3. **电源模式↔风扇方案绑定**：插电/电池或 EC 三档各记一套上限/曲线（对齐 mrid6-fan-gui profiles）。  
4. **保持「上限优先、曲线进阶」**：与 zhaob1n 实测一致，勿把软件曲线当默认。  
5. **不双开** JL16ProAiFan / nbfc 类温控守护。

---

## 5. 推荐产品分层（给本 Fork）

```
日常（默认）
  EC 模式 = 均衡
  WMI 转速上限 = 3500（范围内）
  温度墙 = 92–95
  CustomMode 按需；游戏档用软件预设 78/108

进阶
  CustomMode + 自选 SPL/fPPT/墙
  AutoFan 软件曲线（知悉与 EC 双轨）
  SMU CO / GPU 偏移

专项外挂
  A：充电阈值 NativeEnable/Disable
  C：仅极客刷 EC（本程序不集成）
```

---

## 6. 结论

- **E** 是「官方协议子集 + 重 UI」，风扇本质是上限滑块；本机 0.3.10 还带 Init 刷屏等坏味道。  
- **B** 是目前对 MRID6 **协议最清晰的证据库**；本项目应优先按它纠正风扇语义与模式范围。  
- **A** 补上控制台生态长期缺失的 **电池健康** 能力，宜外挂/安全封装，不宜硬揉进 WinRing0。  
- **C** 是固件级永久改曲线，与用户态控制台正交且高风险。  
- **D（本 Fork）** 在功能广度（SMU/OSD/保活/预设）上已超过 E，下一阶段价值在于：**用 B 的协议正确性收紧风扇与 CustomMode，用 A 补电池，明确不碰 C。**
